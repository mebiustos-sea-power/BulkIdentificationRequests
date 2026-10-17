using System;
using System.Diagnostics;
using BepInEx.Logging;

namespace BulkIdentificationRequests;

internal static class ModLog
{
    private static ManualLogSource? source;
    internal static void Info(string message) => Write(LogLevel.Info, message);
    internal static void Error(string message, Exception error) => Write(LogLevel.Error, message + ": " + error);

    private static void Write(LogLevel level, string message)
    {
        try { (source ??= Logger.CreateLogSource(ModInfo.Name)).Log(level, message); }
        catch (Exception error)
        {
            try { Trace.TraceError(ModInfo.Name + ": " + message + "; logging failed: " + error); }
            catch (Exception) { }
        }
    }
}
