param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity"
$sdk = Join-Path $project "Packages\xreal-sdk\com.xreal.xr.tar.gz"
$model = Join-Path $project "models\hand_landmarker.task"
$lensProbe = Join-Path $project "Assets\Plugins\Android\xreal-private-lens-probe.aar"
$nativeProbe = Join-Path $root "scripts\xreal-compat\native\arm64-v8a\libmlomega_secure_task_probe.so"
$taskStubs = Join-Path $root "scripts\xreal-compat\taskorganizer-stubs.jar"
$prepareLog = Join-Path $project "xreelos-prepare.log"
$log = Join-Path $project "xreelos-build.log"

foreach ($required in @(
    $Unity,
    $sdk,
    $model,
    $lensProbe,
    $nativeProbe,
    $taskStubs
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required build input missing: $required"
    }
}

function Invoke-UnityBatch {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$LogFile
    )
    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-buildTarget", "Android",
        "-projectPath", ('"' + $project + '"'),
        "-executeMethod", $Method,
        "-logFile", ('"' + $LogFile + '"')
    )
    $process = Start-Process `
        -FilePath $Unity `
        -ArgumentList $arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    return $process.ExitCode
}

# A fresh clone needs one Unity pass to import XREAL/AR Foundation and set the
# XREAL_SDK_PRESENT define before the real adapter can compile.
$prepareExit = Invoke-UnityBatch `
    -Method "MLOmega.XR.Editor.AndroidBuildXreal.PrepareDefines" `
    -LogFile $prepareLog
if ($prepareExit -ne 0) {
    Get-Content -LiteralPath $prepareLog -Tail 120 -ErrorAction SilentlyContinue
    throw "Unity preparation failed with exit code $prepareExit"
}

$buildExit = Invoke-UnityBatch `
    -Method "MLOmega.XR.Editor.AndroidBuildXreal.BuildXReelOsApk" `
    -LogFile $log
if ($buildExit -ne 0) {
    Get-Content -LiteralPath $log -Tail 120 -ErrorAction SilentlyContinue
    throw "Unity build failed with exit code $buildExit"
}

$apk = Join-Path $project "build\android\XReelOs-v2.apk"
if (-not (Test-Path -LiteralPath $apk)) { throw "APK was not produced: $apk" }
$release = Join-Path $root "releases\XReelOs-v2.apk"
Copy-Item -LiteralPath $apk -Destination $release -Force
$releaseFiles = @(
    (Join-Path $root "releases\XReelOs.apk"),
    $release
) | Where-Object { Test-Path -LiteralPath $_ }
$hashLines = foreach ($releaseFile in $releaseFiles) {
    $entryHash = Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256
    "$($entryHash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($releaseFile))"
}
[IO.File]::WriteAllText(
    (Join-Path $root "releases\SHA256SUMS.txt"),
    (($hashLines -join "`n") + "`n"),
    [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $release
Get-FileHash -LiteralPath $release -Algorithm SHA256
