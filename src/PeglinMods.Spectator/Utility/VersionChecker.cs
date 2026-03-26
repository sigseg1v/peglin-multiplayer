using BepInEx.Logging;
using UnityEngine;

namespace PeglinMods.Spectator.Utility;

public sealed class VersionChecker
{
    private readonly ManualLogSource _log;

    public string CompiledGameVersion => SpectatorPluginInfo.COMPILED_GAME_VERSION;
    public string ModVersion => SpectatorPluginInfo.VERSION;
    public string RuntimeGameVersion { get; private set; }
    public bool IsVersionMatch { get; private set; }

    public VersionChecker(ManualLogSource log)
    {
        _log = log;
    }

    public void Check()
    {
        RuntimeGameVersion = Application.version ?? "unknown";
        IsVersionMatch = RuntimeGameVersion == CompiledGameVersion;

        _log.LogInfo($"Mod version: {ModVersion}");
        _log.LogInfo($"Compiled against Peglin: {CompiledGameVersion}");
        _log.LogInfo($"Running Peglin: {RuntimeGameVersion}");

        if (!IsVersionMatch)
        {
            _log.LogWarning("========================================");
            _log.LogWarning($"GAME VERSION MISMATCH");
            _log.LogWarning($"  Mod compiled for: {CompiledGameVersion}");
            _log.LogWarning($"  Game running:     {RuntimeGameVersion}");
            _log.LogWarning($"  Some features may not work correctly.");
            _log.LogWarning("========================================");
        }
    }
}
