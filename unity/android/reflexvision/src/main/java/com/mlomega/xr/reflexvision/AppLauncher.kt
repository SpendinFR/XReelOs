package com.mlomega.xr.reflexvision

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log

/**
 * MLOmega V19 — E33.
 *
 * Real Android app-launch Intents behind the PC `open_app` device command (§4).
 * Called from Unity via [AppLauncherBridge] (JNI `CallStatic`). Every method is a
 * pure `(Context, String) -> Boolean`: it builds a concrete [Intent] and returns
 * whether an activity was actually started — the caller surfaces the result as a
 * UIReceipt, so a missing app degrades honestly instead of crashing the session.
 *
 * Choices (ADR §E33):
 *  * **Maps** — `google.navigation:q=<dest>` for turn-by-turn when a destination is
 *    given, else `geo:0,0?q=<dest>` to show a place; empty destination just opens
 *    the Maps home (`geo:0,0`). Falls back to any maps-capable app via ACTION_VIEW.
 *  * **YouTube** — `vnd.youtube:` scheme for the native app on a search, with an
 *    ACTION_VIEW `https://www.youtube.com/results?search_query=` fallback so it
 *    still works when the app is absent (opens the browser).
 *  * **arbitrary package** — `packageManager.getLaunchIntentForPackage(pkg)`.
 */
object AppLauncher {

    private const val TAG = "AppLauncher"

    @JvmStatic
    fun openMaps(context: Context, destination: String): Boolean {
        val dest = destination.trim()
        // Turn-by-turn navigation when a destination is named; otherwise open Maps.
        val primaryUri = when {
            dest.isEmpty() -> Uri.parse("geo:0,0")
            else -> Uri.parse("google.navigation:q=" + Uri.encode(dest))
        }
        val nav = Intent(Intent.ACTION_VIEW, primaryUri).apply {
            setPackage("com.google.android.apps.maps")
        }
        if (start(context, nav)) return true
        // Fallback: any maps app via a geo: query (no package pin).
        val geo = Intent(Intent.ACTION_VIEW, Uri.parse("geo:0,0?q=" + Uri.encode(dest)))
        return start(context, geo)
    }

    @JvmStatic
    fun openYouTube(context: Context, query: String): Boolean {
        val q = query.trim()
        // Native app first via the vnd.youtube scheme (search or home).
        val nativeUri = if (q.isEmpty()) Uri.parse("vnd.youtube:")
        else Uri.parse("vnd.youtube:results?search_query=" + Uri.encode(q))
        val app = Intent(Intent.ACTION_VIEW, nativeUri).apply {
            setPackage("com.google.android.youtube")
        }
        if (start(context, app)) return true
        // Fallback: ACTION_VIEW on the web results URL (browser / chooser).
        val webUrl = if (q.isEmpty()) "https://www.youtube.com/"
        else "https://www.youtube.com/results?search_query=" + Uri.encode(q)
        return start(context, Intent(Intent.ACTION_VIEW, Uri.parse(webUrl)))
    }

    @JvmStatic
    fun openPackage(context: Context, packageName: String): Boolean {
        val pkg = packageName.trim()
        if (pkg.isEmpty()) return false
        val launch = context.packageManager.getLaunchIntentForPackage(pkg)
        if (launch == null) {
            Log.w(TAG, "no launch intent for package: $pkg")
            return false
        }
        return start(context, launch)
    }

    private fun start(context: Context, intent: Intent): Boolean {
        return try {
            // Launched from a non-activity context (Unity plugin) -> NEW_TASK.
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(intent)
            true
        } catch (e: ActivityNotFoundException) {
            Log.w(TAG, "no activity for intent: ${intent.data}")
            false
        } catch (e: Exception) {
            Log.w(TAG, "launch failed: ${e.message}")
            false
        }
    }
}
