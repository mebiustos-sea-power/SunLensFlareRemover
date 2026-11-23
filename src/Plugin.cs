using System;
using System.IO;
using AnchorChain;
using BepInEx.Logging;
using HarmonyLib;
using SeaPower;

namespace SunLensFlareRemover;

[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    private static readonly ManualLogSource Log = Logger.CreateLogSource(ModInfo.Name);
    private static bool initialized;

    public void TriggerEntryPoint()
    {
        if (initialized) return;
        initialized = true;
        var harmony = new Harmony(ModInfo.Id);
        try
        {
            string package = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
                ?? throw new InvalidOperationException("The mod package directory is unavailable.");
            if (!ModActivation.IsEnabled(typeof(FileManager), package))
            {
                Log.LogInfo("Disabled in the mod manager; no patches installed.");
                return;
            }
            SunFlarePatch.Install(harmony);
            Log.LogInfo("Initialized " + ModInfo.Version + ". Sun lens flare removal is active.");
        }
        catch (Exception error)
        {
            try { harmony.UnpatchSelf(); }
            catch (Exception rollbackError) { Log.LogError("Patch rollback failed: " + rollbackError); }
            Log.LogError("Initialization failed; sun lens flare removal is unavailable: " + error);
        }
    }
}
