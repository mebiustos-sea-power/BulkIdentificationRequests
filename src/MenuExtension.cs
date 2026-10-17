using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SeaPower;
using SeapowerUI.ViewModels;
using UniRx;

namespace BulkIdentificationRequests;

internal static class MenuExtension
{
    internal static readonly MethodInfo Target = typeof(ObjectBaseViewModel).GetMethod(nameof(ObjectBaseViewModel.GetContextMenuItems))!;

    internal static void Install(Harmony harmony) =>
        harmony.Patch(Target, postfix: new HarmonyMethod(typeof(MenuExtension), nameof(Postfix)));

    private static void Postfix(ObjectBase ____objectBase, TrulyObservableCollection<ContextMenuItem> __result)
    {
        try
        {
            if (!BulkRequests.IsPlayerUnit(____objectBase)) return;
            if (!LanguageResourceHandler.InstanceExists(false)) throw new InvalidOperationException("The language handler is unavailable.");
            var language = LanguageResourceHandler.Instance;
            var text = ModText.ForLanguage(language.Language);
            ObjectBase sender = ____objectBase;
            var enabled = new ReactiveProperty<bool>(VanillaRequests.Ready && BulkRequests.CanSend(sender)).ToReadOnlyReactiveProperty();
            var entry = new ContextMenuItem(text.Label, command: new DelegateCommand(_ => BulkRequests.Execute(sender)), enabled: enabled, tooltip: text.Tooltip);
            Insert(__result, language.getText("ContextMenu", "Orders", true, true),
                language.getText("ContextMenu", "Orders_Identify", true, true), entry);
        }
        catch (Exception error) { ModLog.Error("Could not add the bulk identification menu item", error); }
    }

    internal static void Insert(IList<ContextMenuItem> roots, string ordersLabel, string identifyLabel, ContextMenuItem entry)
    {
        var orders = roots.SingleOrDefault(item => Equals(item.Label, ordersLabel))
            ?? throw new InvalidOperationException("The unit's Orders menu was not found.");
        var identify = orders.Items?.SingleOrDefault(item => Equals(item.Label, identifyLabel))
            ?? throw new InvalidOperationException("The unit's Identify order was not found.");
        orders.Items!.Insert(orders.Items.IndexOf(identify) + 1, entry);
    }
}
