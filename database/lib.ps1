#Requires -Version 5.1
# Shared native psql transport. Keep this source ASCII for Windows PowerShell 5.1.

function ConvertTo-TrackingNativeArgument {
    param([AllowEmptyString()] [string] $Value)
    # ProcessStartInfo.Arguments is also supported by .NET Framework. Quote one
    # Windows CRT argument, including backslashes before quotes and at its end.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-TrackingPsql {
    [CmdletBinding()]
    param(
        [string] $PsqlPath = 'psql',
        [AllowEmptyString()] [string] $InputText,
        [string] $SqlFile,
        [string] $InputFile,
        [hashtable] $Variables = @{}
    )

    if ([string]::IsNullOrWhiteSpace($env:PGSERVICE) -and
        ([string]::IsNullOrWhiteSpace($env:PGHOST) -or
         [string]::IsNullOrWhiteSpace($env:PGDATABASE) -or
         [string]::IsNullOrWhiteSpace($env:PGUSER))) {
        throw 'Set PGSERVICE, or all of PGHOST, PGDATABASE and PGUSER. Use a protected PGPASSFILE for passwords.'
    }
    if ($PSBoundParameters.ContainsKey('InputText') -and ($SqlFile -or $InputFile)) {
        throw 'Supply InputText, or SqlFile with optional InputFile, not both.'
    }
    if ($InputFile -and -not $SqlFile) {
        throw 'CSV input requires a separate, trusted SqlFile.'
    }
    if (-not $SqlFile -and -not $PSBoundParameters.ContainsKey('InputText')) {
        throw 'Supply InputText or a trusted SqlFile.'
    }
    $executable = @(Get-Command -Name $PsqlPath -CommandType Application -ErrorAction Stop)
    if ($executable.Count -ne 1) {
        throw 'PsqlPath must identify exactly one native psql executable.'
    }

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $sql = [Text.StringBuilder]::new()
    foreach ($name in ($Variables.Keys | Sort-Object)) {
        if ($name -cnotmatch '^[a-z][a-z0-9_]*$' -or $null -eq $Variables[$name]) {
            throw 'SQL variable names must be lowercase identifiers, and values must not be null.'
        }
        $value = [string] $Variables[$name]
        if ($value.Contains([string] [char] 0)) { throw 'SQL variable values must not contain NUL characters.' }
        # Do not send Unicode values through Windows native argv/code pages.
        # Hex-encoded UTF-8 data becomes a psql variable via a single-row query.
        # Only validated identifiers and hex digits are interpolated into SQL.
        $hex = [BitConverter]::ToString($utf8.GetBytes($value)).Replace('-', '')
        [void] $sql.AppendLine("SELECT convert_from(decode('$hex', 'hex'), 'UTF8') AS $name \gset")
    }
    if ($SqlFile) {
        [void] $sql.Append([IO.File]::ReadAllText((Get-Item -LiteralPath $SqlFile -ErrorAction Stop).FullName, $utf8))
    } else {
        [void] $sql.Append($InputText)
    }
    [void] $sql.AppendLine()

    $inputBytes = $utf8.GetBytes($sql.ToString())
    $inputOffset = 0
    if ($InputFile) {
        # A single validated snapshot avoids file changes between validation and
        # import. Never pipe Get-Content to psql: PS 5.1 may re-encode it as ASCII.
        $inputBytes = [IO.File]::ReadAllBytes((Get-Item -LiteralPath $InputFile -ErrorAction Stop).FullName)
        if ($inputBytes.Length -ge 2 -and
            (($inputBytes[0] -eq 0xff -and $inputBytes[1] -eq 0xfe) -or
             ($inputBytes[0] -eq 0xfe -and $inputBytes[1] -eq 0xff))) {
            throw 'CSV must be UTF-8, not UTF-16. Export or save it explicitly as UTF-8.'
        }
        if ($inputBytes.Length -ge 3 -and $inputBytes[0] -eq 0xef -and $inputBytes[1] -eq 0xbb -and $inputBytes[2] -eq 0xbf) {
            $inputOffset = 3
        }
        if ($inputBytes.Length -eq $inputOffset) {
            throw 'CSV is empty or BOM-only. The required column header must be present, even for an empty baseline.'
        }
        try {
            $csv = $utf8.GetString($inputBytes, $inputOffset, $inputBytes.Length - $inputOffset)
        } catch [Text.DecoderFallbackException] {
            throw 'CSV is not valid UTF-8. Export or save it explicitly as UTF-8.'
        }
        if ($csv.Contains([string] [char] 0)) { throw 'CSV contains NUL characters; UTF-16 and binary files are not supported. Use UTF-8.' }
        if ($csv -match '(?m)^\\\.\r?$') {
            throw 'CSV contains a psql end-of-copy marker on its own line; refusing incomplete import.'
        }
    }

    $temporarySql = $null
    $process = [Diagnostics.Process]::new()
    try {
        $arguments = @('-X', '--no-password', '--set=ON_ERROR_STOP=1', '--set=VERBOSITY=terse')
        if ($InputFile) {
            # SQL commands and CSV must use separate inputs. Only the trusted
            # SQL plus encoded variables go in this temporary script; CSV stays
            # on pstdin and can never be interpreted as SQL or psql commands.
            $temporarySql = [IO.Path]::GetTempFileName()
            [IO.File]::WriteAllText($temporarySql, $sql.ToString(), $utf8)
            $arguments += '--file=' + $temporarySql
        } else {
            $arguments += '--file=-'
        }
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executable[0].Source
        $startInfo.Arguments = ($arguments | ForEach-Object { ConvertTo-TrackingNativeArgument $_ }) -join ' '
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardInput = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.StandardOutputEncoding = $utf8
        $startInfo.StandardErrorEncoding = $utf8
        # Inherit libpq connection/passfile settings; never put secrets in argv.
        $startInfo.EnvironmentVariables['PGCLIENTENCODING'] = 'UTF8'
        if ([string]::IsNullOrWhiteSpace($env:PGCONNECT_TIMEOUT)) {
            $startInfo.EnvironmentVariables['PGCONNECT_TIMEOUT'] = '15'
        }
        $process.StartInfo = $startInfo
        if ($null -ne $startInfo.PSObject.Properties['StandardInputEncoding']) {
            $startInfo.StandardInputEncoding = $utf8
            [void] $process.Start()
        } else {
            # .NET Framework uses Console.InputEncoding when creating its stdin
            # writer. Its initial AutoFlush can emit a BOM before any raw writes.
            # Set BOM-less UTF-8 only during Start, then restore the caller's state.
            $previousInputEncoding = [Console]::InputEncoding
            try {
                [Console]::InputEncoding = $utf8
                [void] $process.Start()
            } finally {
                [Console]::InputEncoding = $previousInputEncoding
            }
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $writeFailure = $null
        try {
            $process.StandardInput.BaseStream.Write($inputBytes, $inputOffset, $inputBytes.Length - $inputOffset)
        } catch [IO.IOException] {
            # psql can reject SQL and close its pipe before consuming the input.
            # Report its real error below rather than masking it with pipe errors.
            $writeFailure = $_
        } finally {
            $process.StandardInput.BaseStream.Close()
        }
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "psql failed with exit code $($process.ExitCode): $($errorOutput.Trim())"
        }
        if ($null -ne $writeFailure) { throw $writeFailure }
        if (-not [string]::IsNullOrWhiteSpace($errorOutput)) { Write-Verbose $errorOutput.Trim() }
        return $output
    } finally {
        $process.Dispose()
        if ($null -ne $temporarySql) { [IO.File]::Delete($temporarySql) }
    }
}
