using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BulkIdentificationRequests;
using HarmonyLib;
using SeapowerUI;

internal static class GamePatchTests
{
    public sealed class Selection
    {
        public object Selected = new object();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public object GetSelectedObject() => Selected;
    }
    public sealed class Request
    {
        public Selection Selection = new Selection();
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public object CommunicationResult() => Selection.GetSelectedObject();
    }
    private static readonly MethodInfo selected = typeof(Selection).GetMethod(nameof(Selection.GetSelectedObject))!;
    private static readonly MethodInfo resolver = typeof(GamePatchTests).GetMethod(nameof(ResolveSender))!;
    public static object ResolveSender(Selection selection, Request request) =>
        RequestSource<object, Request>.For(request) ?? selection.GetSelectedObject();

    internal static void Run()
    {
        // Validate discovery and delegate binding against the installed game.
        // Detouring that game method in this process would initialize Unity's
        // native-only Globals; exercise the actual rewrite/detour on this managed
        // sender boundary, while GameCallbackFixture executes the game's IL.
        var menu = typeof(MapUnitViewModel).GetMethod(nameof(MapUnitViewModel.GetContextMenuItems))!;
        var callback = VanillaRequests.FindCallback(VanillaRequests.ReadOriginal(menu));
        _ = (Action<MapUnitViewModel, object>)Delegate.CreateDelegate(typeof(Action<MapUnitViewModel, object>), callback);
        var gameCommunication = VanillaRequests.ReadOriginal(VanillaRequests.Communication);
        _ = VanillaRequests.Rewrite(gameCommunication, VanillaRequests.SelectedObject, VanillaRequests.Resolver);

        var harmony = new Harmony("mebiustos.bulkidentificationrequests.tests");
        var method = typeof(Request).GetMethod(nameof(Request.CommunicationResult))!;
        var request = new Request(); var clicked = new object();
        object selectedUnit = request.Selection.Selected;
        try
        {
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(GamePatchTests), nameof(Transpile)));
            Program.Require(ReferenceEquals(request.CommunicationResult(), selectedUnit), "A manual request must retain its normal sender under a real Harmony detour.");
            RequestSource<object, Request>.Run(clicked, request, () =>
                Program.Require(ReferenceEquals(request.CommunicationResult(), clicked), "The production transpiler must route the scoped sender when installed by Harmony."));
            Program.Require(ReferenceEquals(request.Selection.Selected, selectedUnit) && ReferenceEquals(request.CommunicationResult(), selectedUnit), "Detouring must not modify selection or leave a sender override behind.");
        }
        finally { harmony.UnpatchSelf(); }
        RequestSource<object, Request>.Run(clicked, request, () =>
            Program.Require(ReferenceEquals(request.CommunicationResult(), selectedUnit), "Removing the patch must restore the original sender behavior."));
        Console.WriteLine("PASS game Mono: installed-game callback binding and compatibility; production sender rewrite/detour/rollback on a managed boundary.");
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions) =>
        VanillaRequests.Rewrite(instructions, selected, resolver);
}
