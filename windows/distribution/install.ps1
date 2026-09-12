[CmdletBinding()]
param([switch]$DesktopShortcut)
$ErrorActionPreference = 'Stop'
function Test-WebView2Runtime {
    $clientRoots = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients',
        'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients'
    )
    foreach ($clientRoot in $clientRoots) {
        if (-not (Test-Path -LiteralPath $clientRoot)) { continue }
        foreach ($client in Get-ChildItem -LiteralPath $clientRoot -ErrorAction SilentlyContinue) {
            $properties = Get-ItemProperty -LiteralPath $client.PSPath -ErrorAction SilentlyContinue
            if ($properties.name -like '*WebView2*' -and $properties.pv -and $properties.pv -ne '0.0.0.0') { return $true }
        }
    }
    return $false
}
$source = [IO.Path]::GetFullPath($PSScriptRoot)
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ShuiMan'))
if (-not (Test-Path -LiteralPath (Join-Path $source 'ShuiMan.exe'))) { throw 'Keep install.ps1 beside ShuiMan.exe.' }
if (-not (Test-WebView2Runtime)) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'Install Microsoft Edge WebView2 Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and retry.'
    }
    Write-Output 'Installing Microsoft WebView2 Evergreen Runtime with Windows Package Manager...'
    & winget install --id Microsoft.EdgeWebView2Runtime --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0 -or -not (Test-WebView2Runtime)) {
        throw 'WebView2 installation did not complete. Finish Windows permission prompts or install the runtime from the Microsoft website, then retry.'
    }
}
if ($source.TrimEnd('\') -ne $destination.TrimEnd('\')) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
}
Set-Content -LiteralPath (Join-Path $destination '.shuiman-install') -Value 'ShuiMan Windows portable distribution' -Encoding utf8
$shell = New-Object -ComObject WScript.Shell
$startMenu = [Environment]::GetFolderPath('Programs')
$shortcutPaths = @((Join-Path $startMenu 'ShuiMan.lnk'))
if ($DesktopShortcut) { $shortcutPaths += Join-Path ([Environment]::GetFolderPath('Desktop')) 'ShuiMan.lnk' }
foreach ($shortcutPath in $shortcutPaths) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $destination 'ShuiMan.exe'
    $shortcut.WorkingDirectory = $destination
    $shortcut.Description = 'ShuiMan comic reader'
    $shortcut.Save()
}
Write-Output "Installed to $destination. Launch ShuiMan from the Start menu."
