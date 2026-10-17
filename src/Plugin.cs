using System;
using System.IO;
using AnchorChain;
using HarmonyLib;
using SeaPower;

namespace BulkIdentificationRequests;

[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
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
                ModLog.Info("Disabled in the mod manager; no patches installed.");
                return;
            }
            VanillaRequests.Install(harmony);
            MenuExtension.Install(harmony);
            ModLog.Info("Initialized " + ModInfo.Version + ". Standard identification request rules are preserved.");
        }
        catch (Exception error)
        {
            VanillaRequests.Disable();
            try { harmony.UnpatchSelf(); }
            catch (Exception rollbackError) { ModLog.Error("Patch rollback failed", rollbackError); }
            ModLog.Error("Initialization failed; bulk requests are unavailable", error);
        }
    }
}
