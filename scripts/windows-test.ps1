[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Fixtures
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'windows\ShuiMan.Checks\ShuiMan.Checks.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK, then open a new PowerShell window.'
}
$arguments = @('run', '--project', $project, '--configuration', $Configuration)
if ($Fixtures) { $arguments += @('--', '--fixtures', [IO.Path]::GetFullPath($Fixtures)) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Windows checks exited with code $LASTEXITCODE." }
