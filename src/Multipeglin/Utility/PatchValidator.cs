using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace Multipeglin.Utility;

/// <summary>
/// Scans all [HarmonyPatch] declarations in our assembly and verifies that
/// each target type+method actually exists at runtime. Reports any that are
/// missing, which indicates the game was updated and methods were renamed/removed.
/// </summary>
public static class PatchValidator
{
    /// <summary>
    /// Returns a list of "TypeName.MethodName" strings for patch targets
    /// that could not be found at runtime.
    /// </summary>
    public static List<string> FindMissingPatchTargets(ManualLogSource log)
    {
        var missing = new List<string>();
        var assembly = Assembly.GetExecutingAssembly();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types in our assembly couldn't load — game types they reference are gone
            types = ex.Types.Where(t => t != null).ToArray();
            foreach (var loaderEx in ex.LoaderExceptions?.Where(e => e != null).Distinct()
                     ?? Array.Empty<Exception>())
            {
                var msg = loaderEx.Message ?? "Unknown type load error";
                log?.LogWarning($"[PatchValidator] Type load failed: {msg}");
                missing.Add(msg);
            }
        }

        foreach (var type in types)
        {
            try
            {
                var classAttrs = type.GetCustomAttributes(typeof(HarmonyPatch), false)
                    .Cast<HarmonyPatch>().ToArray();
                if (classAttrs.Length == 0)
                {
                    continue;
                }

                // Extract class-level target info
                Type classTarget = null;
                string classMethodName = null;
                foreach (var a in classAttrs)
                {
                    if (a.info.declaringType != null)
                    {
                        classTarget = a.info.declaringType;
                    }

                    if (a.info.methodName != null)
                    {
                        classMethodName = a.info.methodName;
                    }
                }

                // If class fully specifies a target (like PlayButtonAwakePatch), check it
                if (classTarget != null && classMethodName != null)
                {
                    if (!HasMember(classTarget, classMethodName))
                    {
                        missing.Add($"{classTarget.Name}.{classMethodName}");
                    }
                }

                // Check method-level [HarmonyPatch] attributes
                var methods = type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (var method in methods)
                {
                    var methodAttrs = method.GetCustomAttributes(typeof(HarmonyPatch), false)
                        .Cast<HarmonyPatch>().ToArray();
                    if (methodAttrs.Length == 0)
                    {
                        continue;
                    }

                    // Merge class + method level info (method overrides class)
                    var target = classTarget;
                    var methodName = classMethodName;
                    foreach (var a in methodAttrs)
                    {
                        if (a.info.declaringType != null)
                        {
                            target = a.info.declaringType;
                        }

                        if (a.info.methodName != null)
                        {
                            methodName = a.info.methodName;
                        }
                    }

                    if (target != null && methodName != null)
                    {
                        if (!HasMember(target, methodName))
                        {
                            missing.Add($"{target.Name}.{methodName}");
                        }
                    }
                }
            }
            catch
            {
                // Skip types whose attributes can't be inspected
            }
        }

        return missing.Distinct().ToList();
    }

    private static bool HasMember(Type type, string name)
    {
        return type.GetMember(name,
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic).Length > 0;
    }
}
