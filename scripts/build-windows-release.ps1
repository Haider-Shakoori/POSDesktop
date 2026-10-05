param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use x.y.z semantic format."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$workRoot = Join-Path $outputRoot "_work"
$publishRoot = Join-Path $workRoot "publish"
$desktopPublish = Join-Path $publishRoot "desktop"
$serverPublish = Join-Path $publishRoot "server"
$updaterPublish = Join-Path $publishRoot "updater"

$desktopProject = Join-Path $repoRoot "src/BusinessOS.POS.Desktop/BusinessOS.POS.Desktop.csproj"
$serverProject = Join-Path $repoRoot "src/BusinessOS.POS.LocalServer/BusinessOS.POS.LocalServer.csproj"
$updaterProject = Join-Path $repoRoot "src/BusinessOS.POS.Updater/BusinessOS.POS.Updater.csproj"
$installerScript = Join-Path $repoRoot "packaging/windows/BusinessOS.POS.iss"

if (Test-Path $outputRoot) { Remove-Item $outputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outputRoot, $workRoot, $publishRoot | Out-Null

function Find-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER) -and (Test-Path $env:INNO_SETUP_COMPILER)) {
        return $env:INNO_SETUP_COMPILER
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 7\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 6\ISCC.exe")
    )

    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Find-SignTool {
    $kits = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Windows Kits\10\bin"
    if (-not (Test-Path $kits)) { return $null }

    return Get-ChildItem $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Sign-File([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($env:BUSINESSOS_POS_SIGNING_PFX_BASE64)) { return }

    $signTool = Find-SignTool
    if ([string]::IsNullOrWhiteSpace($signTool)) { throw "Windows SignTool was not found." }

    $pfxPath = Join-Path $workRoot "codesign.pfx"
    if (-not (Test-Path $pfxPath)) {
        [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:BUSINESSOS_POS_SIGNING_PFX_BASE64))
    }

    & $signTool sign /fd SHA256 /td SHA256 /tr "http://timestamp.digicert.com" /f $pfxPath /p $env:BUSINESSOS_POS_SIGNING_PFX_PASSWORD $Path
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed for $Path" }
}

$iscc = Find-Iscc
if ([string]::IsNullOrWhiteSpace($iscc)) { throw "Inno Setup compiler ISCC.exe was not found." }

dotnet publish $desktopProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -o $desktopPublish
dotnet publish $updaterProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -o $updaterPublish
dotnet publish $serverProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:Version=$Version -p:PublishReadyToRun=true -o $serverPublish

foreach ($required in @(
    (Join-Path $desktopPublish "BusinessOS.POS.exe"),
    (Join-Path $updaterPublish "BusinessOS.POS.Updater.exe"),
    (Join-Path $serverPublish "BusinessOS.POS.LocalServer.exe"))) {
    if (-not (Test-Path $required)) { throw "Missing publish artifact: $required" }
}

$embeddedUpdater = Join-Path $desktopPublish "Updater"
New-Item -ItemType Directory -Path $embeddedUpdater -Force | Out-Null
Copy-Item (Join-Path $updaterPublish "*") $embeddedUpdater -Recurse -Force

$modes = @(
    @{ Mode = "Standalone"; Label = "Standalone"; File = "Standalone" },
    @{ Mode = "Server"; Label = "Main Server"; File = "MainServer" },
    @{ Mode = "Client"; Label = "Client Terminal"; File = "ClientTerminal" }
)

$releaseFiles = @()

foreach ($item in $modes) {
    $payloadRoot = Join-Path $workRoot "$($item.File)\payload"
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
    Copy-Item (Join-Path $desktopPublish "*") $payloadRoot -Recurse -Force
    Set-Content -Path (Join-Path $payloadRoot "deployment-default.txt") -Value $item.Mode -Encoding ascii

    if ($item.Mode -eq "Server") {
        $serverTarget = Join-Path $payloadRoot "Server"
        New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
        Copy-Item (Join-Path $serverPublish "*") $serverTarget -Recurse -Force
    }

    Get-ChildItem $payloadRoot -Filter *.exe -Recurse | ForEach-Object { Sign-File $_.FullName }

    $setupBase = "BusinessOS-POS-$($item.File)-Setup-$Version-$RuntimeIdentifier"
    & $iscc "/DMyAppVersion=$Version" "/DSourceDir=$payloadRoot" "/DDeploymentMode=$($item.Mode)" "/DModeLabel=$($item.Label)" "/DOutputDir=$outputRoot" "/DOutputBaseFilename=$setupBase" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $($item.Mode)." }

    $setup = Join-Path $outputRoot "$setupBase.exe"
    if (-not (Test-Path $setup)) { throw "Expected installer missing: $setup" }
    Sign-File $setup

    $portable = Join-Path $outputRoot "BusinessOS-POS-$($item.File)-$Version-$RuntimeIdentifier.zip"
    Compress-Archive -Path (Join-Path $payloadRoot "*") -DestinationPath $portable -CompressionLevel Optimal

    $releaseFiles += $setup, $portable
}

$checksums = foreach ($file in $releaseFiles) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($file))"
}
$checksums | Set-Content (Join-Path $outputRoot "SHA256SUMS.txt") -Encoding ascii

$manifest = [ordered]@{
    product = "BusinessOS POS"
    publisher = "BusinessOS.af"
    version = $Version
    runtime = $RuntimeIdentifier
    self_contained = $true
    generated_at = [DateTimeOffset]::UtcNow.ToString("O")
    artifacts = $releaseFiles | ForEach-Object {
        $info = Get-Item $_
        [ordered]@{
            file = $info.Name
            bytes = $info.Length
            sha256 = (Get-FileHash $_ -Algorithm SHA256).Hash
        }
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputRoot "release-manifest.json") -Encoding utf8

Get-ChildItem $outputRoot -File | Sort-Object Name | Format-Table Name, Length -AutoSize
