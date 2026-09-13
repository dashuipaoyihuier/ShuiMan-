[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
$compilerVersion = '6.7.3'
$downloadUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expectedSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if (-not $Destination) { $Destination = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6' }
$Destination = [IO.Path]::GetFullPath($Destination)
if ($Destination.IndexOfAny([char[]]@('"', "`r", "`n")) -ge 0) { throw 'Invalid compiler installation path.' }
$compilerPath = Join-Path $Destination 'ISCC.exe'
$cacheDirectory = Join-Path ([IO.Path]::GetDirectoryName($Destination)) 'ShuiMan-Compiler-Cache'
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
$downloadPath = Join-Path $cacheDirectory "innosetup-$compilerVersion.exe"
if (-not (Test-Path -LiteralPath $downloadPath) -or (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedSha256) {
    Write-Output "Downloading Inno Setup $compilerVersion from the official GitHub release..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
}
if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedSha256) {
    throw 'Inno Setup download SHA-256 does not match the pinned official 6.7.3 installer.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $downloadPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*O=Pyrsys B.V.,*') {
    throw 'Inno Setup installer must have a valid Pyrsys B.V. Authenticode signature.'
}
$logPath = Join-Path $cacheDirectory 'compiler-install.log'
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/TASKS=""', "/DIR=`"$Destination`"", "/LOG=`"$logPath`"")
$process = Start-Process -FilePath $downloadPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Compiler installation exited with $($process.ExitCode). See $logPath." }
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Compiler installation did not produce ISCC.exe.' }
$compilerSignature = Get-AuthenticodeSignature -LiteralPath $compilerPath
if ($compilerSignature.Status -ne 'Valid' -or $compilerSignature.SignerCertificate.Subject -notlike '*O=Pyrsys B.V.,*') {
    throw 'Installed ISCC.exe does not have a valid Pyrsys B.V. signature.'
}
Write-Output "Inno Setup $compilerVersion installed: $compilerPath"
