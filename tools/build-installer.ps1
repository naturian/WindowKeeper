param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [string]$CompilerDir = (Join-Path $env:TEMP 'InnoSetup-7.0.2')
)

$ErrorActionPreference = 'Stop'
$innoVersion = '7.0.2'
$expectedHash = '5AD54CA3DEF786F8F4212552E54CC6D8D61329E2D24A1CFEE0571D42C2684FF1'
$downloadUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-7_0_2/innosetup-7.0.2-x64.exe'
$compiler = Join-Path $CompilerDir 'ISCC.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Path $CompilerDir -Force | Out-Null
    $installer = Join-Path $env:TEMP "innosetup-$innoVersion-x64.exe"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $installer

    $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Inno Setup SHA-256 mismatch. Expected $expectedHash, received $actualHash."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notlike 'CN=Pyrsys B.V.*') {
        throw "Inno Setup has no valid Pyrsys B.V. Authenticode signature."
    }

    $install = Start-Process -FilePath $installer -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/DIR=$CompilerDir"
    )
    if ($install.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) {
        throw "Inno Setup installation failed with exit code $($install.ExitCode)."
    }
}

$resolvedSource = (Resolve-Path -LiteralPath $SourceDir).Path
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDir).Path
$script = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\installer\WindowKeeper.iss')).Path

& $compiler /Qp "/DMyAppVersion=$Version" "/DSourceDir=$resolvedSource" "/DOutputDir=$resolvedOutput" $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $resolvedOutput "WindowKeeper-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Expected installer was not created: $setup"
}

Write-Output $setup
