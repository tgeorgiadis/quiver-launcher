package com.quiverlauncher.app

import android.app.Activity
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageInfo
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.result.ActivityResult
import androidx.core.content.ContextCompat
import app.tauri.annotation.ActivityCallback
import app.tauri.annotation.Command
import app.tauri.annotation.InvokeArg
import app.tauri.annotation.TauriPlugin
import app.tauri.plugin.Invoke
import app.tauri.plugin.JSObject
import app.tauri.plugin.Plugin
import java.io.File

@InvokeArg
class ApkArgs {
  lateinit var path: String
}

@InvokeArg
class PackageArgs {
  lateinit var packageName: String
}

@InvokeArg
class UrlArgs {
  lateinit var url: String
}

/**
 * Installs, starts and removes the Android apps Quiver downloads, through
 * the system's package installer. android.rs calls it.
 */
@TauriPlugin
class ApkPlugin(private val activity: Activity) : Plugin(activity) {
  private val packages get() = activity.packageManager

  /**
   * Installs an APK. Android asks the player first: to allow Quiver to
   * install apps, the first time, and then to confirm this install.
   */
  @Command
  fun install(invoke: Invoke) {
    if (mayInstall()) {
      commit(invoke)
    } else {
      val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${activity.packageName}"))
      startActivityForResult(invoke, intent, "allowed")
    }
  }

  @ActivityCallback
  fun allowed(invoke: Invoke, result: ActivityResult) {
    if (mayInstall()) {
      commit(invoke)
    } else {
      invoke.reject("Allow Quiver Launcher to install apps (Install unknown apps in Android's settings), then try again.")
    }
  }

  /** Starts an installed app. */
  @Command
  fun launch(invoke: Invoke) {
    val name = invoke.parseArgs(PackageArgs::class.java).packageName
    if (installed(name) == null) {
      return invoke.reject("This app isn't installed on this device any more. Get it again to play it.")
    }
    val intent = packages.getLaunchIntentForPackage(name)
      ?: packages.getLeanbackLaunchIntentForPackage(name)
      ?: return invoke.reject("This app has nothing to open; Android didn't find a screen to start it on.")
    activity.startActivity(intent)
    invoke.resolve()
  }

  /** Asks Android to uninstall an app; rejects with "cancelled" when the player says no. */
  @Command
  fun uninstall(invoke: Invoke) {
    val name = invoke.parseArgs(PackageArgs::class.java).packageName
    if (installed(name) == null) return invoke.resolve()
    startActivityForResult(invoke, Intent(Intent.ACTION_DELETE, Uri.parse("package:$name")), "uninstalled")
  }

  @ActivityCallback
  fun uninstalled(invoke: Invoke, result: ActivityResult) {
    if (installed(invoke.parseArgs(PackageArgs::class.java).packageName) == null) invoke.resolve() else invoke.reject("cancelled")
  }

  /** Opens a web page in the browser. */
  @Command
  fun openUrl(invoke: Invoke) {
    val url = invoke.parseArgs(UrlArgs::class.java).url
    try {
      activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
      invoke.resolve()
    } catch (e: Exception) {
      invoke.reject("Couldn't open your browser.")
    }
  }

  /** Puts Quiver behind the other apps, as Home does, rather than closing it. */
  @Command
  fun toBackground(invoke: Invoke) {
    activity.moveTaskToBack(true)
    invoke.resolve()
  }

  private fun mayInstall() = Build.VERSION.SDK_INT < Build.VERSION_CODES.O || packages.canRequestPackageInstalls()

  private fun installed(name: String): PackageInfo? =
    try {
      packages.getPackageInfo(name, 0)
    } catch (e: PackageManager.NameNotFoundException) {
      null
    }

  private fun archiveInfo(path: String): PackageInfo? = packages.getPackageArchiveInfo(path, 0)

  private fun describe(info: PackageInfo) = JSObject().put("packageName", info.packageName).put("versionName", info.versionName)

  /** Hands the APK to the package installer, which reports back through a broadcast. */
  private fun commit(invoke: Invoke) {
    val path = invoke.parseArgs(ApkArgs::class.java).path
    Thread {
      try {
        val file = File(path)
        val info = archiveInfo(path) ?: return@Thread invoke.reject("This file isn't an Android app (APK).")
        val installer = packages.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        params.setAppPackageName(info.packageName)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
          // Updates to apps Quiver installed then go in without asking again.
          params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val id = installer.createSession(params)
        val action = "${activity.packageName}.INSTALL_RESULT.$id"
        ContextCompat.registerReceiver(activity, Result(invoke, info), IntentFilter(action), ContextCompat.RECEIVER_NOT_EXPORTED)
        installer.openSession(id).use { session ->
          file.inputStream().use { input ->
            session.openWrite("base.apk", 0, file.length()).use { output ->
              input.copyTo(output)
              session.fsync(output)
            }
          }
          val flags = PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0)
          val done = PendingIntent.getBroadcast(activity, id, Intent(action).setPackage(activity.packageName), flags)
          session.commit(done.intentSender)
        }
      } catch (e: Exception) {
        invoke.reject("Couldn't install the app: ${e.message}")
      }
    }.start()
  }

  /** The package installer's answer for one install. */
  private inner class Result(private val invoke: Invoke, private val info: PackageInfo) : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
      val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
      if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
        @Suppress("DEPRECATION")
        val confirm = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
          intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
        } else {
          intent.getParcelableExtra(Intent.EXTRA_INTENT)
        }
        confirm?.let { activity.startActivity(it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
        return
      }
      activity.unregisterReceiver(this)
      when (status) {
        PackageInstaller.STATUS_SUCCESS -> invoke.resolve(describe(info))
        PackageInstaller.STATUS_FAILURE_ABORTED -> invoke.reject("cancelled")
        PackageInstaller.STATUS_FAILURE_CONFLICT ->
          invoke.reject("A different copy of this app is already installed (signed by someone else, or newer). Uninstall it from Android's settings, then try again.")
        PackageInstaller.STATUS_FAILURE_INCOMPATIBLE -> invoke.reject("This app doesn't run on this device.")
        PackageInstaller.STATUS_FAILURE_STORAGE -> invoke.reject("There isn't enough space on this device to install it.")
        PackageInstaller.STATUS_FAILURE_BLOCKED -> invoke.reject("This device blocked installing the app.")
        else -> invoke.reject("Android couldn't install the app: ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE) ?: "no reason given"}.")
      }
    }
  }
}
