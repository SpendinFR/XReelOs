[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$AndroidSdk = "",
    [string]$JavaHome = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "..\unity"
}
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)

if ([string]::IsNullOrWhiteSpace($AndroidSdk)) {
    $AndroidSdk = $env:MLOMEGA_ANDROID_SDK
}
if ([string]::IsNullOrWhiteSpace($AndroidSdk)) {
    $AndroidSdk = Join-Path $env:LOCALAPPDATA "Android\Sdk"
}

if ([string]::IsNullOrWhiteSpace($JavaHome)) {
    $JavaHome = $env:MLOMEGA_ANDROID_JDK
}
if ([string]::IsNullOrWhiteSpace($JavaHome)) {
    $unityJava = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK"
    if (Test-Path -LiteralPath $unityJava) {
        $JavaHome = $unityJava
    } elseif ($env:JAVA_HOME) {
        $JavaHome = $env:JAVA_HOME
    }
}
if ([string]::IsNullOrWhiteSpace($JavaHome)) {
    throw "JDK not found. Pass -JavaHome or set JAVA_HOME."
}

$package = Join-Path $ProjectPath "Packages\xreal-sdk\com.xreal.xr.tar.gz"
$pluginRoot = Join-Path $ProjectPath `
    "Library\PackageCache\com.xreal.xr\Runtime\Plugins\Android"
$displayAar = Join-Path $pluginRoot "GlassesDisplayPlugEvent-2.4.2.aar"
$activityAar = Join-Path $pluginRoot "nractivitylife_6-release.aar"
$source = Join-Path $PSScriptRoot `
    "xreal-compat\com\xreal\glassesdisplayplugevent\display\DisplayModel.java"
$fakeActivityLayout = Join-Path $PSScriptRoot `
    "xreal-compat\ai\nreal\activitylife\activity_nrfake.xml"
$androidJar = Join-Path $AndroidSdk "platforms\android-34\android.jar"
$javac = Join-Path $JavaHome "bin\javac.exe"
$javap = Join-Path $JavaHome "bin\javap.exe"
$jar = Join-Path $JavaHome "bin\jar.exe"

foreach ($required in @(
    $package,
    $displayAar,
    $activityAar,
    $source,
    $fakeActivityLayout,
    $androidJar,
    $javac,
    $javap,
    $jar
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "[XREAL S24 compat] Required file missing: $required"
    }
}

$work = Join-Path $ProjectPath "Temp\MLOmegaXrealDisplayCompat"
if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    # Always restore both vendor AARs before applying our one demonstrated
    # compatibility fix. This prevents an older build from silently retaining
    # experimental lifecycle/fullscreen modifications.
    & tar.exe -xf $package -C $work `
        "package/Runtime/Plugins/Android/GlassesDisplayPlugEvent-2.4.2.aar" `
        "package/Runtime/Plugins/Android/nractivitylife_6-release.aar"
    if ($LASTEXITCODE -ne 0) {
        throw "[XREAL S24 compat] Could not extract official SDK AARs"
    }
    $officialRoot = Join-Path $work "package\Runtime\Plugins\Android"
    Copy-Item `
        -LiteralPath (Join-Path $officialRoot "nractivitylife_6-release.aar") `
        -Destination $activityAar `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $officialRoot "GlassesDisplayPlugEvent-2.4.2.aar") `
        -Destination $displayAar `
        -Force

    $classes = Join-Path $work "classes"
    $archive = Join-Path $work "display-aar"
    $activityArchive = Join-Path $work "activity-aar"
    New-Item -ItemType Directory -Path `
        $classes,$archive,$activityArchive -Force | Out-Null

    & $javac `
        -encoding UTF-8 `
        -source 8 `
        -target 8 `
        -classpath $androidJar `
        -d $classes `
        $source
    if ($LASTEXITCODE -ne 0) {
        throw "[XREAL S24 compat] DisplayModel javac failed: $LASTEXITCODE"
    }

    Push-Location $archive
    try {
        & $jar xf $displayAar
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $archive "classes.jar"))) {
            throw "[XREAL S24 compat] Could not extract display AAR"
        }
        & $jar uf (Join-Path $archive "classes.jar") `
            -C $classes `
            "com/xreal/glassesdisplayplugevent/display/DisplayModel.class"
        if ($LASTEXITCODE -ne 0) {
            throw "[XREAL S24 compat] Could not replace DisplayModel.class"
        }
        & $jar uf $displayAar classes.jar
        if ($LASTEXITCODE -ne 0) {
            throw "[XREAL S24 compat] Could not update display AAR"
        }
    }
    finally {
        Pop-Location
    }

    # The stock proxy activity uses Android's desktop background drawable.
    # On Samsung DeX that drawable is purple and becomes the opaque slab seen
    # behind both eye views. Black emits no light on the optical display and is
    # therefore the correct see-through clear colour.
    Push-Location $activityArchive
    try {
        & $jar xf $activityAar
        $activityLayoutPath = Join-Path `
            $activityArchive "res\layout\activity_nrfake.xml"
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath $activityLayoutPath)) {
            throw "[XREAL S24 compat] Could not extract activity lifecycle AAR"
        }
        Copy-Item `
            -LiteralPath $fakeActivityLayout `
            -Destination $activityLayoutPath `
            -Force
        & $jar uf $activityAar "res/layout/activity_nrfake.xml"
        if ($LASTEXITCODE -ne 0) {
            throw "[XREAL S24 compat] Could not replace proxy activity layout"
        }
    }
    finally {
        Pop-Location
    }

    $verification = (
        & $javap `
            -classpath (Join-Path $archive "classes.jar") `
            -c `
            -p `
            "com.xreal.glassesdisplayplugevent.display.DisplayModel" 2>&1
    ) -join "`n"
    if ($verification -notmatch "getDeviceProductInfo" -or
        $verification -notmatch "identifyXrealDisplay") {
        throw "[XREAL S24 compat] EDID display patch verification failed"
    }

    Write-Host (
        "[XREAL S24 compat] Official SDK lifecycle restored; EDID display-name " +
        "compatibility and optical-black proxy layout patches are active.")
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
