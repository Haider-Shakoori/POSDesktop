param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$ArtifactsDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsDirectory))
$installDir = Join-Path $env:ProgramFiles "BusinessOS\POS"
$verifyPath = Join-Path $env:TEMP "businessos-pos-install-verify.json"

function Install-Setup([string]$Setup) {
    $process = Start-Process $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        throw "Setup failed with exit code $($process.ExitCode): $Setup"
    }
}

function Verify-Installed([string]$ExpectedMode) {
    $exe = Join-Path $installDir "BusinessOS.POS.exe"
    if (-not (Test-Path $exe)) { throw "BusinessOS.POS.exe is missing." }

    if (Test-Path $verifyPath) { Remove-Item $verifyPath -Force }
    $process = Start-Process $exe -ArgumentList "--verify-install=$verifyPath" -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Install verification exited with $($process.ExitCode)." }

    $result = Get-Content $verifyPath -Raw | ConvertFrom-Json
    if ($result.product -ne "BusinessOS POS") { throw "Unexpected product identity." }
    if ($result.version -ne $Version) { throw "Installed version '$($result.version)' does not match '$Version'." }
    if ($result.deployment_hint -ne $ExpectedMode) { throw "Deployment mode '$($result.deployment_hint)' does not match '$ExpectedMode'." }

    if (-not (Test-Path (Join-Path $installDir "Updater\BusinessOS.POS.Updater.exe"))) {
        throw "Updater agent is missing."
    }

    if ($ExpectedMode -eq "Server" -and -not (Test-Path (Join-Path $installDir "Server\BusinessOS.POS.LocalServer.exe"))) {
        throw "Main Server package is missing the LAN server executable."
    }

    if ($ExpectedMode -ne "Server" -and (Test-Path (Join-Path $installDir "Server\BusinessOS.POS.LocalServer.exe"))) {
        throw "Non-server package unexpectedly contains the LAN server executable."
    }
}

function Uninstall-App {
    $uninstaller = Get-ChildItem $installDir -Filter "unins*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $uninstaller) { throw "Uninstaller not found." }

    $process = Start-Process $uninstaller.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) { throw "Uninstall failed with exit code $($process.ExitCode)." }
}

foreach ($mode in @(
    @{ Name = "Standalone"; Expected = "Standalone" },
    @{ Name = "MainServer"; Expected = "Server" },
    @{ Name = "ClientTerminal"; Expected = "Client" })) {

    $setup = Join-Path $artifacts "BusinessOS-POS-$($mode.Name)-Setup-$Version-win-x64.exe"
    if (-not (Test-Path $setup)) { throw "Missing release artifact: $setup" }

    Install-Setup $setup
    Verify-Installed $mode.Expected
    Uninstall-App

    if (Test-Path (Join-Path $installDir "BusinessOS.POS.exe")) {
        throw "Application executable remained after uninstall."
    }
}

$manifest = Get-Content (Join-Path $artifacts "release-manifest.json") -Raw | ConvertFrom-Json
if ($manifest.self_contained -ne $true) { throw "Release must be self-contained." }

foreach ($artifact in $manifest.artifacts) {
    $path = Join-Path $artifacts $artifact.file
    if (-not (Test-Path $path)) { throw "Manifest artifact missing: $($artifact.file)" }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($actual -ne $artifact.sha256) { throw "Checksum mismatch: $($artifact.file)" }
}

Write-Host "BusinessOS POS installer smoke tests passed."
