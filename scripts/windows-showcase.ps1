[CmdletBinding()]
param(
    [string]$Output = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $repository 'build\windows-showcase' }
$target = [System.IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) {
    throw "Showcase output already exists. Choose a new -Output directory: $target"
}
$project = Join-Path $repository 'windows\ShuiMan.UiChecks\ShuiMan.UiChecks.csproj'
& dotnet run --project $project --configuration $Configuration -- --showcase $target
if ($LASTEXITCODE -ne 0) { throw "Showcase generation exited with code $LASTEXITCODE." }
