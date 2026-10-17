using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeaPower;
using SeapowerUI;

namespace BulkIdentificationRequests;

internal static class VanillaRequests
{
    internal static readonly MethodInfo Communication = typeof(MapUnitViewModel).GetMethod("CommunicationResult",
        new[] { typeof(Vehicle), typeof(string), typeof(ObjectBase).MakeByRefType() })!;
    internal static readonly MethodInfo SelectedObject = typeof(RenderPosition).GetMethod("getSelectedObject", Type.EmptyTypes)!;
    internal static readonly MethodInfo Resolver = typeof(VanillaRequests).GetMethod(nameof(ResolveSender), BindingFlags.NonPublic | BindingFlags.Static)!;
    internal static MethodInfo? Callback { get; private set; }
    private static Action<MapUnitViewModel, object>? request;
    internal static bool Ready { get; private set; }

    internal static void Install(Harmony harmony)
    {
        var menu = typeof(MapUnitViewModel).GetMethod(nameof(MapUnitViewModel.GetContextMenuItems))!;
        Callback = FindCallback(ReadOriginal(menu));
        request = (Action<MapUnitViewModel, object>)Delegate.CreateDelegate(typeof(Action<MapUnitViewModel, object>), Callback);
        harmony.Patch(Communication, transpiler: new HarmonyMethod(typeof(VanillaRequests), nameof(Transpile)));
        if (!Ready) throw new InvalidOperationException("The standard request's sender lookup could not be patched.");
    }

    // Harmony's bundled assembly references Mono's mscorlib ILGenerator.
    // Reflect this reader boundary so the plugin can target netstandard2.1.
    internal static List<CodeInstruction> ReadOriginal(MethodBase method)
    {
        var reader = typeof(PatchProcessor).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == "GetOriginalInstructions" && candidate.GetParameters().Length == 2 &&
                !candidate.GetParameters()[1].ParameterType.IsByRef);
        return ((IEnumerable<CodeInstruction>)(reader.Invoke(null, new object?[] { method, null })
            ?? throw new InvalidOperationException("The original game instructions are unavailable."))).ToList();
    }

    internal static void Disable() { Ready = false; request = null; }

    internal static MethodInfo FindCallback(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var markers = code.Select((instruction, index) => (instruction, index))
            .Where(row => row.instruction.opcode == OpCodes.Ldstr && Equals(row.instruction.operand, "RequestIdentify")).ToArray();
        if (markers.Length != 1) throw new InvalidOperationException("The standard identification menu entry is missing or ambiguous.");
        var body = code.Skip(markers[0].index + 1).TakeWhile(instruction =>
            instruction.opcode != OpCodes.Newobj || !(instruction.operand is ConstructorInfo constructor) ||
            constructor.DeclaringType != typeof(SeapowerUI.ViewModels.ContextMenuItem)).ToArray();
        var callbacks = body.Where(instruction => instruction.opcode == OpCodes.Ldftn)
            .Select(instruction => instruction.operand).OfType<MethodInfo>().ToArray();
        if (callbacks.Length != 1) throw new InvalidOperationException("The standard identification callback is missing or ambiguous.");
        var callback = callbacks[0];
        if (callback.IsStatic || callback.DeclaringType != typeof(MapUnitViewModel) || callback.ReturnType != typeof(void) ||
            !callback.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object) }))
            throw new InvalidOperationException("Unsupported standard identification callback signature.");
        return callback;
    }

    internal static void Send(ObjectBase sender, MapUnitViewModel target)
    {
        if (!Ready || request == null) throw new InvalidOperationException("Bulk identification requests are unavailable.");
        // Invoke the actual game callback, including communication checks, compliance and disclosure.
        RequestSource<ObjectBase, MapUnitViewModel>.Run(sender, target, () => request(target, null!));
    }

    internal static ObjectBase ResolveSender(RenderPosition renderPosition, MapUnitViewModel target) =>
        RequestSource<ObjectBase, MapUnitViewModel>.For(target) ?? renderPosition.getSelectedObject();

    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodInfo selectedObject, MethodInfo resolver)
    {
        var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        var sites = code.Select((instruction, index) => (instruction, index))
            .Where(row => row.instruction.Calls(selectedObject)).Select(row => row.index).ToArray();
        if (sites.Length != 1) throw new InvalidOperationException("Unsupported CommunicationResult sender lookup; the game or another mod changed it.");
        int site = sites[0];
        if (code[site].blocks.Count != 0) throw new InvalidOperationException("The communication sender lookup crosses an exception boundary.");
        var target = new CodeInstruction(OpCodes.Ldarg_0);
        code[site].MoveLabelsTo(target);
        code[site] = target;
        code.Insert(site + 1, new CodeInstruction(OpCodes.Call, resolver));
        return code;
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        try
        {
            var rewritten = Rewrite(original, SelectedObject, Resolver);
            Ready = true;
            return rewritten;
        }
        catch (Exception error)
        {
            Ready = false;
            ModLog.Error("Communication compatibility error; bulk requests have been disabled", error);
            return original;
        }
    }
}
