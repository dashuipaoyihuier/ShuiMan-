[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'windows\ShuiMan.UiChecks\ShuiMan.UiChecks.csproj'
& dotnet run --project $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Windows UI checks exited with code $LASTEXITCODE." }
