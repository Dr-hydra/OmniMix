param(
    [string]$Version,
    [string]$InnoSetupPath,
    [switch]$SkipVCRedistCheck
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path "$ScriptDir\.."

$InnoSetupCandidates = @(
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$InnoSetup = if ($InnoSetupPath) {
    $InnoSetupPath
} else {
    $InnoSetupCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
$IssFile = "$ScriptDir\installer\OmniMixPlayer.iss"
$PlayerBuildDir = "$ProjectRoot\playerbuild"
$OutputDir = "$ProjectRoot\release"
$VCRedistFile = "$PlayerBuildDir\VC_redist.x64.exe"

if (-not $Version) {
    $VersionInfoFile = "$PlayerBuildDir\version_info.json"
    if (Test-Path $VersionInfoFile) {
        $VersionInfo = Get-Content $VersionInfoFile -Raw | ConvertFrom-Json
        $Version = $VersionInfo.flutter_version -replace '\+.*$', ''
    }
    if (-not $Version) {
        $Version = "4.2.1"
    }
}

Write-Host "OmniMixPlayer installer build $Version"
Write-Host "Output: $OutputDir"

if (-not $InnoSetup -or -not (Test-Path $InnoSetup)) {
    Write-Host "[ERROR] Inno Setup compiler was not found." -ForegroundColor Red
    Write-Host "Install it with: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $PlayerBuildDir)) {
    Write-Host "[ERROR] Missing playerbuild directory: $PlayerBuildDir" -ForegroundColor Red
    exit 1
}

$RequiredFiles = @(
    "$PlayerBuildDir\OmniMixPlayer.Gui.Vbnet.exe",
    "$PlayerBuildDir\OmniMixPlayer.Backend.exe"
)
foreach ($File in $RequiredFiles) {
    if (-not (Test-Path $File)) {
        Write-Host "[ERROR] Missing required file: $File" -ForegroundColor Red
        exit 1
    }
}

if (-not $SkipVCRedistCheck -and -not (Test-Path $VCRedistFile)) {
    $VCRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
    Write-Host "Downloading VC++ runtime..."
    Invoke-WebRequest -Uri $VCRedistUrl -OutFile $VCRedistFile -UseBasicParsing
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$SourceDirArg = (Resolve-Path $PlayerBuildDir).Path.Replace("\", "/")
$ReleaseDirArg = (Resolve-Path $OutputDir).Path.Replace("\", "/")
$Arguments = @(
    "/DMyAppVersion=$Version",
    "/DSourceDir=$SourceDirArg",
    "/DReleaseDir=$ReleaseDirArg",
    "`"$IssFile`""
)

$Process = Start-Process -FilePath $InnoSetup -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
if ($Process.ExitCode -ne 0) {
    Write-Host "[ERROR] Installer compilation failed: $($Process.ExitCode)" -ForegroundColor Red
    exit $Process.ExitCode
}

Write-Host "[OK] $OutputDir\OmniMixPlayer_V${Version}_VBNet_installer.exe" -ForegroundColor Green
