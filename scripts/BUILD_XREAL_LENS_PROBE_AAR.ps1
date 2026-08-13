param(
  [string]$PrivateLibrary = "",
  [string]$Output = "",
  [string]$ExpectedSha256 = "D87965AAE92FC07A61F4A4542A88D698C406FC3849D9274248746B580E357135"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$module = Join-Path $root "unity\android\xreal-lens-probe"
if (-not $PrivateLibrary) {
  throw "Pass -PrivateLibrary with libnr_service.so from your licensed, matching ControlGlasses package."
}
if (-not $Output) {
  $Output = Join-Path $root `
    "unity\Assets\Plugins\Android\xreal-private-lens-probe.aar"
}
$PrivateLibrary = [IO.Path]::GetFullPath($PrivateLibrary)
$Output = [IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath $PrivateLibrary -PathType Leaf)) {
  throw "libnr_service.so is missing: $PrivateLibrary"
}
$expected = $ExpectedSha256.Trim().ToUpperInvariant()
if ($expected -notmatch '^[0-9A-F]{64}$') {
  throw "ExpectedSha256 must contain exactly 64 hexadecimal characters."
}
$actual = (Get-FileHash -LiteralPath $PrivateLibrary -Algorithm SHA256).Hash
if ($actual -ne $expected) {
  throw "Unexpected libnr_service.so: $actual (expected $expected). Pass the explicitly reviewed vendor hash with -ExpectedSha256 when testing another licensed ControlGlasses runtime."
}

$unityAndroid = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Data\PlaybackEngines\AndroidPlayer"
$jdk = if ($env:JAVA_HOME -and (Test-Path -LiteralPath $env:JAVA_HOME)) {
  $env:JAVA_HOME
} else {
  Join-Path $unityAndroid "OpenJDK"
}
$javac = Join-Path $jdk "bin\javac.exe"
$jar = Join-Path $jdk "bin\jar.exe"
if (-not (Test-Path -LiteralPath $javac)) { throw "javac is missing: $javac" }
if (-not (Test-Path -LiteralPath $jar)) { throw "jar is missing: $jar" }
$androidSdk = if ($env:ANDROID_SDK_ROOT) {
  $env:ANDROID_SDK_ROOT
} else {
  Join-Path $env:LOCALAPPDATA "Android\Sdk"
}
$androidJar = Get-ChildItem `
  (Join-Path $androidSdk "platforms") `
  -Directory -ErrorAction Stop |
  Sort-Object Name -Descending |
  ForEach-Object { Join-Path $_.FullName "android.jar" } |
  Where-Object { Test-Path -LiteralPath $_ } |
  Select-Object -First 1
if (-not $androidJar) { throw "android.jar is missing" }

$work = Join-Path $root "tmp-xreal-lens-probe-aar"
if (Test-Path -LiteralPath $work) {
  Remove-Item -LiteralPath $work -Recurse -Force
}
$classes = Join-Path $work "classes"
$stage = Join-Path $work "aar"
$jni = Join-Path $stage "jni\arm64-v8a"
New-Item -ItemType Directory -Path $classes,$jni -Force | Out-Null

$sources = Get-ChildItem -LiteralPath (Join-Path $module "src\main\java") `
  -Recurse -Filter "*.java" -File | ForEach-Object FullName
& $javac -encoding UTF-8 -source 11 -target 11 `
  -classpath $androidJar -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw "Lens bridge Java compilation failed" }

Push-Location $classes
try { & $jar cf (Join-Path $stage "classes.jar") . }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "classes.jar creation failed" }
Copy-Item -LiteralPath (Join-Path $module "AndroidManifest.xml") `
  -Destination (Join-Path $stage "AndroidManifest.xml")
Copy-Item -LiteralPath $PrivateLibrary `
  -Destination (Join-Path $jni "libnr_service.so")

$outDir = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }
Push-Location $stage
try { & $jar cf $Output . }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Lens bridge AAR creation failed" }

Write-Host "[OK] Local proprietary lens bridge AAR: $Output" -ForegroundColor Green
Write-Host "     libnr_service SHA256=$actual" -ForegroundColor DarkGray
