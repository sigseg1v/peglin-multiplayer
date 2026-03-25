using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace PeglinMods.Core;

/// <summary>
/// Disables the Unity crash reporter so modded crash reports aren't sent to game devs.
/// Uses reflection to avoid hard dependencies on modules that may not exist in all builds.
/// </summary>
public static class CrashReporterDisabler
{
    public static void Disable(ManualLogSource log)
    {
        DisableReportingApi(log);
        DisableReportingExe(log);
    }

    private static void DisableReportingApi(ManualLogSource log)
    {
        try
        {
            // CrashReportHandler lives in UnityEngine.CrashReportingModule which may or may
            // not be loaded. Reflection avoids a hard dependency so the plugin still loads
            // even if this module is stripped in a future build.
            var type = Type.GetType(
                "UnityEngine.CrashReportHandler.CrashReportHandler, UnityEngine.CrashReportingModule");

            if (type == null)
            {
                log.LogDebug("CrashReportHandler type not found - module may not be present");
                return;
            }

            var prop = type.GetProperty("enableCaptureExceptions",
                BindingFlags.Static | BindingFlags.Public);
            if (prop != null)
            {
                prop.SetValue(null, false);
                log.LogInfo("Unity crash report capture disabled");
            }

            // Also try the logBufferSize property to minimize any residual data collection
            var logProp = type.GetProperty("logBufferSize",
                BindingFlags.Static | BindingFlags.Public);
            if (logProp != null)
            {
                logProp.SetValue(null, 0);
            }
        }
        catch (Exception e)
        {
            log.LogWarning($"Could not disable crash reporting API: {e.Message}");
        }
    }

    private static void DisableReportingExe(ManualLogSource log)
    {
        try
        {
            // Application.dataPath is "GameDir/Peglin_Data", so parent is the game root
            var gameDir = Directory.GetParent(Application.dataPath)?.FullName;
            if (gameDir == null) return;

            var crashHandler = Path.Combine(gameDir, "UnityCrashHandler64.exe");
            var disabledPath = crashHandler + ".disabled_by_mods";

            if (File.Exists(crashHandler))
            {
                // Don't overwrite a previous disabled copy
                if (File.Exists(disabledPath))
                    File.Delete(disabledPath);

                File.Move(crashHandler, disabledPath);
                log.LogInfo("Crash handler executable renamed to .disabled_by_mods");
            }
        }
        catch (Exception e)
        {
            // Non-fatal: the API disable above is the primary mechanism.
            // File rename may fail under Proton/Wine permissions.
            log.LogDebug($"Could not rename crash handler exe: {e.Message}");
        }
    }
}
