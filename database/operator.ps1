#Requires -Version 5.1
<#
.SYNOPSIS
Offline invoice-tracking administration using native psql (no Bash or WSL).
.DESCRIPTION
Configure PGSERVICE or PGHOST/PGDATABASE/PGUSER and protected libpq credentials.
Run offline with the required object permissions; the same login may also be used by Frends.
The grant-runtime command is optional and only for a separate existing restricted role.
Commands do not create databases or roles, reset history, or resend files.
.EXAMPLE
.\operator.ps1 -Command register-source -Source momentum-nassjo-production
.EXAMPLE
.\operator.ps1 -Command seed-history -Source momentum-nassjo-production -CsvFile 'C:\Secure\approved history.csv'
.EXAMPLE
.\operator.ps1 -Command report -Source momentum-nassjo-production -PsqlPath 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('register-source', 'seed-history', 'activate-source', 'report', 'confirm-delivery', 'grant-runtime')]
    [string] $Command,
    [string] $Source,
    [string] $CsvFile,
    [string] $BaselineReference,
    [string] $DeliveryId,
    [string] $Filename,
    [string] $ContentSha256,
    [string] $DeliveryReference,
    [string] $RuntimeRole,
    [string] $PsqlPath = 'psql'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

$required = switch ($Command) {
    'register-source' { @('Source') }
    'seed-history' { @('Source', 'CsvFile') }
    'activate-source' { @('Source', 'BaselineReference') }
    'report' { @('Source') }
    'confirm-delivery' { @('Source', 'DeliveryId', 'Filename', 'ContentSha256', 'DeliveryReference') }
    'grant-runtime' { @('RuntimeRole') }
}
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $name -ValueOnly))) {
        throw "Command '$Command' requires a nonblank -$name parameter."
    }
}
$commandParameters = @('Source', 'CsvFile', 'BaselineReference', 'DeliveryId', 'Filename', 'ContentSha256', 'DeliveryReference', 'RuntimeRole')
foreach ($name in $commandParameters) {
    if ($PSBoundParameters.ContainsKey($name) -and $name -notin $required) {
        throw "Parameter -$name is not used by '$Command'; remove it to avoid running an unintended command."
    }
}

$variables = @{}
if ($Command -ne 'grant-runtime') { $variables.source = $Source }
switch ($Command) {
    'activate-source' { $variables.baseline_reference = $BaselineReference }
    'confirm-delivery' {
        $variables.delivery_id = $DeliveryId
        $variables.filename = $Filename
        $variables.content_sha256 = $ContentSha256
        $variables.delivery_reference = $DeliveryReference
    }
    'grant-runtime' { $variables.runtime_role = $RuntimeRole }
}
$parameters = @{
    PsqlPath = $PsqlPath
    SqlFile = Join-Path (Join-Path $PSScriptRoot 'sql') ($Command + '.sql')
    Variables = $variables
}
if ($Command -eq 'seed-history') { $parameters.InputFile = $CsvFile }
Invoke-TrackingPsql @parameters
