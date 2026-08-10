param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity"
$sdk = Join-Path $project "Packages\xreal-sdk\com.xreal.xr.tar.gz"
$model = Join-Path $project "models\hand_landmarker.task"
$log = Join-Path $project "xreelos-build.log"

foreach ($required in @($Unity, $sdk, $model)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required build input missing: $required"
    }
}

& $Unity `
    -batchmode -nographics -quit -buildTarget Android `
    -projectPath $project `
    -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.BuildXReelOsApk `
    -logFile $log

if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $log -Tail 120 -ErrorAction SilentlyContinue
    throw "Unity build failed with exit code $LASTEXITCODE"
}

$apk = Join-Path $project "build\android\XReelOs.apk"
if (-not (Test-Path -LiteralPath $apk)) { throw "APK was not produced: $apk" }
Get-Item -LiteralPath $apk
Get-FileHash -LiteralPath $apk -Algorithm SHA256
