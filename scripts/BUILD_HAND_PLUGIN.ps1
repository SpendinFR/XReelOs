param(
    [string]$Gradle = "",
    [string]$JavaHome = "C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot",
    [string]$AndroidSdk = "$env:LOCALAPPDATA\Android\Sdk"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$module = Join-Path $root "unity\android\reflexvision"

if (-not $Gradle) {
    $repoGradle = Join-Path (Split-Path -Parent $root) ".tools\gradle-8.7\bin\gradle.bat"
    $command = Get-Command gradle -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $repoGradle) { $Gradle = $repoGradle }
    elseif ($command) { $Gradle = $command.Source }
}

foreach ($required in @($Gradle, $JavaHome, $AndroidSdk, $module)) {
    if (-not $required -or -not (Test-Path -LiteralPath $required)) {
        throw "Required hand-plugin build input missing: $required"
    }
}

$env:JAVA_HOME = $JavaHome
$env:ANDROID_HOME = $AndroidSdk
$env:ANDROID_SDK_ROOT = $AndroidSdk

& $Gradle -p $module clean testDebugUnitTest exportUnityRelease
if ($LASTEXITCODE -ne 0) { throw "Hand plugin build failed: $LASTEXITCODE" }

$aar = Join-Path $root "unity\Assets\Plugins\Android\mlomega-reflexvision.aar"
if (-not (Test-Path -LiteralPath $aar)) { throw "AAR was not exported: $aar" }
Get-Item -LiteralPath $aar
Get-FileHash -LiteralPath $aar -Algorithm SHA256
