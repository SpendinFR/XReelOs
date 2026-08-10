// XReel OS hand-tracking Android library.
//
// This public module intentionally contains only the MediaPipe hand pipelines
// and the small Android application launcher used by spatial app windows. It
// does not capture audio and has no ASR, KWS, ONNX or Memory dependency.

plugins {
    id("com.android.library") version "8.5.2"
    id("org.jetbrains.kotlin.android") version "1.9.24"
}

base { archivesName.set("reflexvision") }

android {
    namespace = "com.mlomega.xr.reflexvision"
    compileSdk = 34

    defaultConfig {
        minSdk = 26
        targetSdk = 34
        consumerProguardFiles("consumer-rules.pro")
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions { jvmTarget = "17" }

    packaging {
        resources { excludes += setOf("META-INF/*.kotlin_module") }
    }

    testOptions {
        unitTests { isReturnDefaultValues = true }
    }

    // Kept explicit so a developer building from a working copy that also
    // contains private MLOmega sources cannot accidentally package them.
    sourceSets.named("main") {
        java.exclude(
            "**/Asr*.kt",
            "**/CommandWindow.kt",
            "**/InstantImage*.kt",
            "**/KeywordEncoder.kt",
            "**/MarianTokenizer.kt",
            "**/MicForegroundService.kt",
            "**/OfflineTranslator*.kt",
            "**/PcmFeed.kt",
            "**/SemanticSound*.kt",
            "**/WakeWordMatcher.kt",
        )
    }
    sourceSets.named("test") {
        java.exclude(
            "**/CommandWindowTest.kt",
            "**/KeywordEncoderTest.kt",
            "**/MarianTokenizerTest.kt",
            "**/OfflineTranslator*.kt",
            "**/WakeWordMatcherTest.kt",
        )
    }
}

dependencies {
    implementation("com.google.mediapipe:tasks-vision:0.10.29")
    implementation("androidx.annotation:annotation:1.8.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.mockito:mockito-core:5.11.0")
}

tasks.register<Copy>("exportUnityRelease") {
    dependsOn("assembleRelease")
    into(layout.projectDirectory.dir("../../Assets/Plugins/Android"))
    from(layout.buildDirectory.file("outputs/aar/reflexvision-release.aar")) {
        rename { "mlomega-reflexvision.aar" }
    }
    from(configurations.named("releaseRuntimeClasspath")) {
        include("*.aar", "*.jar")
    }
}
