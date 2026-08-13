param(
    [string]$AndroidSdk = "$env:LOCALAPPDATA\Android\Sdk",
    [string]$JavaHome = ""
)

$ErrorActionPreference = "Stop"
$unityJava = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK"
if ([string]::IsNullOrWhiteSpace($JavaHome)) {
    if (Test-Path -LiteralPath $unityJava) {
        $JavaHome = $unityJava
    } elseif ($env:JAVA_HOME) {
        $JavaHome = $env:JAVA_HOME
    }
}
if ([string]::IsNullOrWhiteSpace($JavaHome)) {
    throw "JDK not found. Pass -JavaHome or set JAVA_HOME."
}
$repo = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $PSScriptRoot "xreal-compat\stubs-src"
$outputRoot = Join-Path $PSScriptRoot "xreal-compat\stubs-build"
$classRoot = Join-Path $outputRoot "classes"
$jarOutput = Join-Path $PSScriptRoot "xreal-compat\taskorganizer-stubs.jar"
$androidJar = Join-Path $AndroidSdk "platforms\android-36\android.jar"
$javac = Join-Path $JavaHome "bin\javac.exe"
$jar = Join-Path $JavaHome "bin\jar.exe"

if (-not (Test-Path -LiteralPath $androidJar)) {
    throw "Android 36 android.jar missing: $androidJar"
}
if (-not (Test-Path -LiteralPath $javac)) {
    throw "javac missing: $javac"
}

if (Test-Path -LiteralPath $classRoot) {
    Remove-Item -LiteralPath $classRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $classRoot -Force | Out-Null

$source = Join-Path $sourceRoot "android\window\TaskOrganizer.java"
& $javac -source 17 -target 17 -classpath $androidJar -d $classRoot $source
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path -LiteralPath $jarOutput) {
    Remove-Item -LiteralPath $jarOutput -Force
}
Push-Location $classRoot
try {
    & $jar --create --file $jarOutput android\window\TaskOrganizer.class
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Host "[OK] compile-only TaskOrganizer signatures: $jarOutput"
