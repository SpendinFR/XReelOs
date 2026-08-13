param(
    [string]$Serial = "",
    [switch]$InstallApk,
    [switch]$V2ThermalCandidate,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
if (-not (Test-Path -LiteralPath $adb)) {
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw "adb not found. Install Android platform-tools." }
    $adb = $command.Source
}

$target = @()
if ($Serial) { $target = @("-s", $Serial) }

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & $script:adb @script:target @Arguments
    if ($LASTEXITCODE -ne 0) { throw "adb failed: $($Arguments -join ' ')" }
}

Invoke-Adb wait-for-device

if ($InstallApk) {
    $apkName = if ($V2ThermalCandidate) { "XReelOs-v2.apk" } else { "XReelOs.apk" }
    $apk = Join-Path $root ("releases\" + $apkName)
    if (-not (Test-Path -LiteralPath $apk)) { throw "APK missing: $apk" }
    Invoke-Adb install -r $apk
}

foreach ($scope in @("system", "global", "secure")) {
    Invoke-Adb shell settings put $scope dex_on_external_display 0
}

$shizukuScript = "/sdcard/Android/data/moe.shizuku.privileged.api/start.sh"
& $adb @target shell test -f $shizukuScript
if ($LASTEXITCODE -eq 0) {
    & $adb @target shell sh $shizukuScript | Out-Host
}

Invoke-Adb shell am force-stop com.spendinfr.xreelos
Invoke-Adb logcat -c

$dex = & $adb @target shell dumpsys activity activities |
    Select-String "SecondaryLauncher|dexservice|mode=freeform|name=Desk"
if ($dex) {
    Write-Warning "Samsung DeX still owns an external-display task. Disable DeX manually before XR launch."
    $dex | ForEach-Object { Write-Warning $_.Line }
}

if (-not $NoLaunch) {
    Invoke-Adb shell am start -n `
        com.spendinfr.xreelos/ai.nreal.activitylife.NRXRActivity
}

Write-Host "XReel OS preflight complete. Keep DeX disabled and connect the glasses."
