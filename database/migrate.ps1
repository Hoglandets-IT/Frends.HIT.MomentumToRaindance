#Requires -Version 5.1
# Apply trusted, append-only migration files outside Frends in one locked transaction.
# Compatible with Windows PowerShell 5.1 and PowerShell 7; no Bash or WSL required.
[CmdletBinding()]
param(
    [string] $PsqlPath = 'psql'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

$migrationDirectory = Join-Path $PSScriptRoot 'migrations'
$migrationNames = [string[]] @(
    Get-ChildItem -LiteralPath $migrationDirectory -File |
        Where-Object { $_.Extension -ieq '.sql' } |
        ForEach-Object { $_.Name }
)
if ($migrationNames.Count -eq 0) {
    throw "No SQL migration files found in '$migrationDirectory'."
}
[Array]::Sort($migrationNames, [StringComparer]::Ordinal)

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$migrations = [System.Collections.Generic.List[object]]::new()
foreach ($name in $migrationNames) {
    if ($name -cnotmatch '^[0-9]{3,}_[a-z0-9_]+\.sql$') {
        throw "Invalid migration filename: '$name'. Expected at least three digits followed by a lowercase name and .sql."
    }

    # Hash the same immutable byte snapshot that is decoded below. Never normalize
    # line endings or rewrite a migration to make its recorded checksum match.
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $migrationDirectory $name))
    if ($bytes.Length -eq 0) {
        throw "Empty migration file: '$name'."
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "Migration '$name' has a UTF-8 BOM. Restore the original UTF-8 file without a BOM; do not modify applied migration history."
    }
    try {
        $content = $utf8.GetString($bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        throw "Migration '$name' is not valid UTF-8. Restore the original migration file."
    }
    if ($content.Contains("`r")) {
        throw "Migration '$name' contains CR line endings. Use the repository's original LF files (see .gitattributes); do not normalize checksums or modify applied migration history."
    }

    $stream = [System.IO.MemoryStream]::new($bytes, $false)
    try {
        $checksum = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
    $migrations.Add([PSCustomObject] @{
        Version = $name.Substring(0, $name.Length - 4)
        Checksum = $checksum
        Content = $content
    })
}

$sql = [System.Text.StringBuilder]::new()
[void] $sql.AppendLine(@'
BEGIN;
SET LOCAL synchronous_commit = on;
SET LOCAL lock_timeout = '60s';
DO $version$
BEGIN
    IF current_setting('server_version_num')::integer < 150000 THEN
        RAISE EXCEPTION 'PostgreSQL 15 or newer is required (historical CSV import validates column headers).';
    END IF;
END
$version$;
-- Stable application-specific transaction lock serializes all migration runners.
SELECT pg_advisory_xact_lock(726149031, 1);
CREATE SCHEMA IF NOT EXISTS momentum_raindance;
REVOKE ALL ON SCHEMA momentum_raindance FROM PUBLIC;
CREATE TABLE IF NOT EXISTS momentum_raindance.schema_migrations (
    version text PRIMARY KEY,
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL DEFAULT now()
);
CREATE TEMP TABLE expected_migrations (version text PRIMARY KEY, checksum text NOT NULL) ON COMMIT DROP;
'@)
foreach ($migration in $migrations) {
    [void] $sql.AppendLine("INSERT INTO expected_migrations VALUES ('$($migration.Version)', '$($migration.Checksum)');")
}
[void] $sql.AppendLine(@'
DO $check$
BEGIN
    IF EXISTS (
        SELECT 1 FROM momentum_raindance.schema_migrations applied
        LEFT JOIN expected_migrations expected USING (version)
        WHERE expected.version IS NULL OR expected.checksum <> applied.checksum
    ) THEN
        RAISE EXCEPTION 'Migration history differs from local files. Restore the original migration files; add a new migration instead of modifying history.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM expected_migrations expected
        WHERE NOT EXISTS (SELECT 1 FROM momentum_raindance.schema_migrations applied WHERE applied.version = expected.version)
          AND expected.version < (SELECT max(version) FROM momentum_raindance.schema_migrations)
    ) THEN
        RAISE EXCEPTION 'New migrations must sort after all applied migrations; out-of-order changes are forbidden.';
    END IF;
END
$check$;
'@)
foreach ($migration in $migrations) {
    [void] $sql.AppendLine("SELECT NOT EXISTS (SELECT 1 FROM momentum_raindance.schema_migrations WHERE version = '$($migration.Version)') AS apply_migration \gset")
    [void] $sql.AppendLine('\if :apply_migration')
    [void] $sql.AppendLine("\echo Applying $($migration.Version)")
    [void] $sql.Append($migration.Content)
    [void] $sql.AppendLine()
    [void] $sql.AppendLine("INSERT INTO momentum_raindance.schema_migrations (version, checksum) VALUES ('$($migration.Version)', '$($migration.Checksum)');")
    [void] $sql.AppendLine('\endif')
}
[void] $sql.AppendLine('COMMIT;')

Invoke-TrackingPsql -PsqlPath $PsqlPath -InputText $sql.ToString()
