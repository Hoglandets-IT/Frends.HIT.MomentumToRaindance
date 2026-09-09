#Requires -Version 5.1
<#
Development-only integration tests. Requires a local PostgreSQL 15+ administrator
connection through PGHOST, PGDATABASE and PGUSER, plus a native psql executable.
Creates/drops only a new, randomly named scratch database and runtime role.
Run in Windows PowerShell 5.1 or PowerShell 7; no Bash, WSL, Docker or Pester.
#>
[CmdletBinding()]
param(
    [string] $PsqlPath = 'psql',
    [switch] $AllowCreateTestDatabase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AllowCreateTestDatabase) {
    throw 'Development only: pass -AllowCreateTestDatabase to permit creation and cleanup of an isolated scratch database and role.'
}
if ($env:PGHOST -notin @('127.0.0.1', 'localhost', '::1') -or
    [string]::IsNullOrWhiteSpace($env:PGDATABASE) -or
    [string]::IsNullOrWhiteSpace($env:PGUSER) -or
    -not [string]::IsNullOrWhiteSpace($env:PGSERVICE)) {
    throw 'Tests require explicit loopback PGHOST, PGDATABASE and PGUSER, without PGSERVICE. Never run against a production database.'
}

$databaseDirectory = Split-Path -Parent $PSScriptRoot
. (Join-Path $databaseDirectory 'lib.ps1')
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$testId = [Guid]::NewGuid().ToString('N')
$testDatabase = 'momentum_windows_test_' + $testId
$runtimeRole = 'momentum_windows_runtime_' + $testId
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('momentum windows tests ' + $testId)
$fixtureDirectory = Join-Path $scratch 'database fixtures with spaces'
$fixtureMigrations = Join-Path $fixtureDirectory 'migrations'
[void] [IO.Directory]::CreateDirectory($fixtureMigrations)
foreach ($name in @('lib.ps1', 'migrate.ps1')) {
    Copy-Item -LiteralPath (Join-Path $databaseDirectory $name) -Destination $fixtureDirectory
}
Get-ChildItem -LiteralPath (Join-Path $databaseDirectory 'migrations') -Filter '*.sql' |
    Copy-Item -Destination $fixtureMigrations
$originalDatabase = $env:PGDATABASE
$databaseCreated = $false
$roleCreated = $false
$assertions = 0
$invocationNumber = 0
$powerShellExecutable = (Get-Process -Id $PID).Path
$savedTempEnvironment = @{}
foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) {
    $savedTempEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Start-TestScript {
    param([string] $ScriptPath, [hashtable] $Parameters)
    # Pass serialized data through an encoded command, not shell interpolation.
    # The child process also verifies that script failures yield a nonzero exit.
    $payload = @{ Script = $ScriptPath; Parameters = $Parameters } | ConvertTo-Json -Compress -Depth 4
    $encodedPayload = [Convert]::ToBase64String($utf8.GetBytes($payload))
    $command = @'
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PAYLOAD__')) | ConvertFrom-Json
    $arguments = @{}
    foreach ($property in $payload.Parameters.PSObject.Properties) { $arguments[$property.Name] = $property.Value }
    $output = & $payload.Script @arguments | Out-String -Width 4096
    [Console]::Out.Write($output)
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
'@
    $command = $command.Replace('__PAYLOAD__', $encodedPayload)
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $powerShellExecutable
    $startInfo.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + $encodedCommand
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    [void] $process.Start()
    return @{
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
    }
}

function Complete-TestScript {
    param([hashtable] $Invocation)
    $process = $Invocation.Process
    try {
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            throw 'A database test command exceeded two minutes.'
        }
        $result = @{
            ExitCode = $process.ExitCode
            Output = $Invocation.StandardOutput.GetAwaiter().GetResult() + $Invocation.StandardError.GetAwaiter().GetResult()
        }
        $script:invocationNumber++
        [IO.File]::WriteAllText((Join-Path $scratch ('command-{0:D3}.log' -f $script:invocationNumber)), $result.Output, $utf8)
        return $result
    } finally {
        $process.Dispose()
    }
}

function Invoke-TestScript {
    param([string] $ScriptPath, [hashtable] $Parameters, [string] $ExpectedFailure)
    $result = Complete-TestScript (Start-TestScript $ScriptPath $Parameters)
    if ($ExpectedFailure) {
        if ($result.ExitCode -eq 0 -or $result.Output -notmatch $ExpectedFailure) {
            throw "Expected a nonzero exit containing '$ExpectedFailure'. Exit: $($result.ExitCode); output: $($result.Output)"
        }
    } elseif ($result.ExitCode -ne 0) {
        throw "Script failed with exit $($result.ExitCode): $($result.Output)"
    }
    $script:assertions++
    return $result.Output
}

function Invoke-Migration {
    param([string] $ExpectedFailure)
    Invoke-TestScript (Join-Path $fixtureDirectory 'migrate.ps1') @{ PsqlPath = $PsqlPath } $ExpectedFailure
}

function Invoke-Operator {
    param([hashtable] $Parameters, [string] $ExpectedFailure)
    $Parameters['PsqlPath'] = $PsqlPath
    Invoke-TestScript (Join-Path $databaseDirectory 'operator.ps1') $Parameters $ExpectedFailure
}

function Assert-Scalar {
    param([string] $Sql, [string] $Expected, [hashtable] $Variables = @{})
    $output = Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText ("\pset tuples_only on`n\pset format unaligned`n" + $Sql + "`n") -Variables $Variables
    $lines = @($output -split '\r?\n' | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -eq 0 -or $lines[-1].Trim() -cne $Expected) {
        throw "SQL assertion expected '$Expected', received '$output'."
    }
    $script:assertions++
}

function Write-TestCsv {
    param([string] $Path, [object[]] $Rows)
    $csv = @('ledger_note_id,invoice_number,ledger_note_number,reference')
    foreach ($row in $Rows) {
        $csv += (@($row | ForEach-Object { '"' + ([string] $_).Replace('"', '""') + '"' }) -join ',')
    }
    [IO.File]::WriteAllText($Path, (($csv -join "`r`n") + "`r`n"), $utf8)
}

try {
    # Force psql's trusted temporary --file argument to contain spaces too, not
    # merely the PowerShell script/CSV paths. Child processes inherit this path.
    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) {
        [Environment]::SetEnvironmentVariable($name, $scratch, 'Process')
    }
    [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText 'CREATE DATABASE :"test_database";' -Variables @{ test_database = $testDatabase })
    $databaseCreated = $true
    $env:PGDATABASE = $testDatabase
    Write-Host "Testing PowerShell $($PSVersionTable.PSVersion) against isolated database $testDatabase."

    # Native .NET Framework initializes redirected stdin using the caller's
    # Console.InputEncoding. Check both a legacy code page and BOM-bearing UTF-8;
    # neither may change SQL bytes or leave the caller's console state altered.
    $savedInputEncoding = [Console]::InputEncoding
    try {
        foreach ($encoding in @([Text.Encoding]::GetEncoding(437), [Text.UTF8Encoding]::new($true))) {
            [Console]::InputEncoding = $encoding
            $beforeEncoding = [Console]::InputEncoding
            $unicodeScalar = 'N' + [char] 0x00e4 + 'ssj' + [char] 0x00f6
            Assert-Scalar ("SELECT '" + $unicodeScalar + "';") $unicodeScalar
            if ([Console]::InputEncoding.CodePage -ne $beforeEncoding.CodePage -or
                [Console]::InputEncoding.GetPreamble().Length -ne $beforeEncoding.GetPreamble().Length) {
                throw 'Native psql invocation changed the caller Console.InputEncoding.'
            }
            $assertions++
        }
    } finally {
        [Console]::InputEncoding = $savedInputEncoding
    }

    # A later failure must roll back the entire initial installation.
    $failingFile = Join-Path $fixtureMigrations '999_deliberate_failure.sql'
    [IO.File]::WriteAllText($failingFile, "CREATE TABLE momentum_raindance.must_rollback (id integer);`nSELECT 1 / 0;`n", $utf8)
    [void] (Invoke-Migration 'division by zero')
    Assert-Scalar "SELECT to_regnamespace('momentum_raindance') IS NULL;" 't'
    Move-Item -LiteralPath $failingFile -Destination (Join-Path $scratch 'failed-migration.fixture')

    # The same transaction lock must serialize concurrent fresh installations.
    [IO.File]::WriteAllText((Join-Path $fixtureMigrations '998_concurrency.sql'), "SELECT pg_sleep(2);`n", $utf8)
    $first = Start-TestScript (Join-Path $fixtureDirectory 'migrate.ps1') @{ PsqlPath = $PsqlPath }
    $second = Start-TestScript (Join-Path $fixtureDirectory 'migrate.ps1') @{ PsqlPath = $PsqlPath }
    $firstResult = Complete-TestScript $first
    $secondResult = Complete-TestScript $second
    if ($firstResult.ExitCode -ne 0 -or $secondResult.ExitCode -ne 0) {
        throw "Concurrent migrations failed: $($firstResult.Output) $($secondResult.Output)"
    }
    $assertions += 2
    $migrationFiles = @(Get-ChildItem -LiteralPath $fixtureMigrations -Filter '*.sql')
    Assert-Scalar 'SELECT count(*) FROM momentum_raindance.schema_migrations;' ([string] $migrationFiles.Count)
    [void] (Invoke-Migration)
    foreach ($migration in $migrationFiles) {
        Assert-Scalar "SELECT checksum FROM momentum_raindance.schema_migrations WHERE version = :'version';" `
            (Get-FileHash -LiteralPath $migration.FullName -Algorithm SHA256).Hash.ToLowerInvariant() `
            @{ version = [IO.Path]::GetFileNameWithoutExtension($migration.Name) }
    }

    $firstMigration = Join-Path $fixtureMigrations '001_invoice_tracking.sql'
    $originalBytes = [IO.File]::ReadAllBytes($firstMigration)
    [IO.File]::AppendAllText($firstMigration, "`n-- checksum drift test`n", $utf8)
    [void] (Invoke-Migration 'Migration history differs')
    [IO.File]::WriteAllBytes($firstMigration, $originalBytes)
    # Checkouts must keep migration bytes unchanged; CRLF is real drift, not normalized silently.
    $originalText = $utf8.GetString($originalBytes)
    [IO.File]::WriteAllText($firstMigration, ($originalText.Replace("`r`n", "`n").Replace("`n", "`r`n")), $utf8)
    [void] (Invoke-Migration 'CR line endings|Migration history differs')
    [IO.File]::WriteAllBytes($firstMigration, $originalBytes)
    $secondMigration = Join-Path $fixtureMigrations '002_canonical_identities.sql'
    $missingFixture = Join-Path $scratch 'missing-migration.fixture'
    Move-Item -LiteralPath $secondMigration -Destination $missingFixture
    [void] (Invoke-Migration 'Migration history differs')
    Move-Item -LiteralPath $missingFixture -Destination $secondMigration
    $outOfOrder = Join-Path $fixtureMigrations '000_out_of_order.sql'
    [IO.File]::WriteAllText($outOfOrder, "SELECT 1;`n", $utf8)
    [void] (Invoke-Migration 'out-of-order changes are forbidden')
    Move-Item -LiteralPath $outOfOrder -Destination (Join-Path $scratch 'out-of-order.fixture')
    [void] (Invoke-Migration)

    # Real UTF-8, CRLF, CSV quotes/newlines and values that must not be damaged by
    # native argument quoting or Windows ANSI-code-page conversion. The helper
    # deliberately carries SQL variables as encoded data, not native argv.
    # Construct non-ASCII characters so this script itself parses in PS 5.1 without a BOM.
    $swedish = 'N' + [char] 0x00e4 + 'ssj' + [char] 0x00f6 + ' ' + [char] 0x00c5
    $source = 'windows-' + $testId + '-' + $swedish + ' & \"O''Brien\"\\tail\'
    $invoiceId = 'invoice-' + $swedish + ' & \"O''Brien\"\\tail\'
    $evidence = $swedish + ' & "O''Brien"\archive, receipt' + "`r`n" + 'second CSV evidence line'
    $history = Join-Path $scratch 'historical invoices with spaces.csv'
    Write-TestCsv $history @(, @($invoiceId, 'invoice-1', 'ledger-1', $evidence))
    $historyHash = (Get-FileHash -LiteralPath $history -Algorithm SHA256).Hash
    [void] (Invoke-Operator @{ Command = 'register-source'; Source = $source })
    [void] (Invoke-Operator @{ Command = 'register-source'; Source = $source })
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source + '-missing'; CsvFile = $history } 'Register the source')

    $invalid = Join-Path $scratch 'invalid history.csv'
    [IO.File]::WriteAllText($invalid, "wrong_header,invoice_number,ledger_note_number,reference`r`nid,,,Evidence`r`n", $utf8)
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'column name mismatch')
    Write-TestCsv $invalid @(, @(' padded-id ', '', '', 'Synthetic'))
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'check constraint')
    Write-TestCsv $invalid @(@('duplicate-id', '', '', 'Synthetic'), @('duplicate-id', '', '', 'Synthetic'))
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'duplicate key')
    [IO.File]::WriteAllText($invalid, "ledger_note_id,invoice_number,ledger_note_number,reference`r`n\.`r`nnot-read,,,Synthetic`r`n", $utf8)
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'end-of-copy marker')
    [IO.File]::WriteAllBytes($invalid, ([IO.File]::ReadAllBytes($history) + [byte[]] @(0xc0, 0xaf)))
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'UTF-8|UTF8')
    [IO.File]::WriteAllBytes($invalid, ([byte[]] @(0xff, 0xfe) + [Text.Encoding]::Unicode.GetBytes([IO.File]::ReadAllText($history, $utf8))))
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'UTF-16|UTF-8|UTF8')
    [IO.File]::WriteAllBytes($invalid, [byte[]] @())
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'CSV is empty or BOM-only')
    [IO.File]::WriteAllBytes($invalid, [byte[]] @(0xef, 0xbb, 0xbf))
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $invalid } 'CSV is empty or BOM-only')
    Assert-Scalar 'SELECT count(*) FROM momentum_raindance.invoices;' '0'

    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $history })
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $history })
    Assert-Scalar 'SELECT count(*) FROM momentum_raindance.invoices;' '1'
    Assert-Scalar "SELECT historical_reference = :'evidence' AND ledger_note_id = :'invoice_id' FROM momentum_raindance.invoices WHERE source_system = :'source';" `
        't' @{ evidence = $evidence; invoice_id = $invoiceId; source = $source }
    if ((Get-FileHash -LiteralPath $history -Algorithm SHA256).Hash -cne $historyHash) {
        throw 'History import modified the input CSV bytes.'
    }
    $assertions++
    $bomHistory = Join-Path $scratch 'bom history.csv'
    [IO.File]::WriteAllBytes($bomHistory, ([byte[]] @(0xef, 0xbb, 0xbf) + [IO.File]::ReadAllBytes($history)))
    $bomHash = (Get-FileHash -LiteralPath $bomHistory -Algorithm SHA256).Hash
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $bomHistory })
    if ((Get-FileHash -LiteralPath $bomHistory -Algorithm SHA256).Hash -cne $bomHash) {
        throw 'Import modified the original BOM-bearing CSV file.'
    }
    $assertions++
    Assert-Scalar 'SELECT count(*) FROM momentum_raindance.invoices;' '1'

    $baseline = $swedish + ' & \"O''Brien\"\\approved baseline\'
    [void] (Invoke-Operator @{ Command = 'activate-source'; Source = $source; BaselineReference = $baseline })
    [void] (Invoke-Operator @{ Command = 'activate-source'; Source = $source; BaselineReference = $baseline })
    [void] (Invoke-Operator @{ Command = 'activate-source'; Source = $source; BaselineReference = 'Conflicting approval' } 'different baseline evidence')
    [void] (Invoke-Operator @{ Command = 'seed-history'; Source = $source; CsvFile = $history } 'only allowed before source activation')
    Assert-Scalar "SELECT baseline_reference = :'baseline' FROM momentum_raindance.sources WHERE source_system = :'source';" 't' @{ source = $source; baseline = $baseline }

    $deliveryId = [Guid]::NewGuid().ToString()
    $filename = '300K24_20260909_123456.txt'
    $contentHash = 'a' * 64
    $reserveSql = @'
BEGIN;
INSERT INTO momentum_raindance.deliveries (delivery_id, source_system, filename, content_sha256, byte_count, invoice_count, last_local_id, status)
VALUES (:'delivery_id', :'source', :'filename', :'content_hash', 100, 1, 5, 'reserved');
INSERT INTO momentum_raindance.invoices (source_system, ledger_note_id, first_local_id, content_sha256, delivery_id)
VALUES (:'source', 'runtime-1', 5, :'content_hash', :'delivery_id');
COMMIT;
'@
    [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText $reserveSql -Variables @{
        delivery_id = $deliveryId; source = $source; filename = $filename; content_hash = $contentHash
    })
    $receipt = $swedish + ' & \"O''Brien\"\\verified receipt\'
    $confirm = @{ Command = 'confirm-delivery'; Source = $source; DeliveryId = $deliveryId; Filename = $filename; ContentSha256 = ('b' * 64); DeliveryReference = $receipt }
    [void] (Invoke-Operator $confirm 'Evidence does not match')
    $confirm.ContentSha256 = $contentHash
    $confirm.Filename = 'wrong-filename.txt'
    [void] (Invoke-Operator $confirm 'Evidence does not match')
    Assert-Scalar "SELECT status FROM momentum_raindance.deliveries WHERE delivery_id = :'delivery_id';" 'reserved' @{ delivery_id = $deliveryId }
    $confirm.Filename = $filename
    [void] (Invoke-Operator $confirm)
    [void] (Invoke-Operator $confirm)
    $confirm.DeliveryReference = 'Conflicting receipt'
    [void] (Invoke-Operator $confirm 'different evidence')
    Assert-Scalar "SELECT status = :'status' AND delivery_reference = :'receipt' FROM momentum_raindance.deliveries WHERE delivery_id = :'delivery_id';" `
        't' @{ status = 'delivered'; receipt = $receipt; delivery_id = $deliveryId }
    $report = Invoke-Operator @{ Command = 'report'; Source = $source }
    if (-not $report.Contains($filename) -or -not $report.Contains($swedish)) { throw 'Report lost delivery data or Unicode text.' }
    $assertions++

    # Verify grants under a uniquely named non-owner role, never a real runtime login.
    [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText 'CREATE ROLE :"runtime_role" NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;' -Variables @{ runtime_role = $runtimeRole })
    $roleCreated = $true
    [void] (Invoke-Operator @{ Command = 'grant-runtime'; RuntimeRole = $env:PGUSER } 'administrative/owner privileges')
    [void] (Invoke-Operator @{ Command = 'grant-runtime'; RuntimeRole = $runtimeRole })
    [void] (Invoke-Operator @{ Command = 'grant-runtime'; RuntimeRole = $runtimeRole })
    $privilegeSql = [IO.File]::ReadAllText((Join-Path $databaseDirectory 'tests/runtime-privileges.sql'), $utf8).Replace('momentum_tracking_runtime', $runtimeRole)
    [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText $privilegeSql)
    $assertions++

    Write-Host "PASS: $assertions assertions. Atomic/concurrent migrations, checksums, CSV, Unicode/quoting, all operator commands and runtime privileges."
    Write-Host "Synthetic test logs retained at: $scratch"
} finally {
    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) {
        [Environment]::SetEnvironmentVariable($name, $savedTempEnvironment[$name], 'Process')
    }
    $env:PGDATABASE = $originalDatabase
    # Only names generated and successfully created by this invocation are eligible.
    if ($databaseCreated -and $testDatabase -cmatch '^momentum_windows_test_[0-9a-f]{32}$') {
        [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText 'DROP DATABASE :"test_database";' -Variables @{ test_database = $testDatabase })
    }
    if ($roleCreated -and $runtimeRole -cmatch '^momentum_windows_runtime_[0-9a-f]{32}$') {
        [void] (Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText 'DROP ROLE :"runtime_role";' -Variables @{ runtime_role = $runtimeRole })
    }
}
