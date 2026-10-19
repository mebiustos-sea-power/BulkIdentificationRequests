using System;
using System.IO;
using System.Linq;
using System.Threading;
using BulkIdentificationRequests;
using SeapowerUI;
using SeapowerUI.ViewModels;

internal static class BehaviorTests
{
    internal static void Run()
    {
        Program.Require(ContactSelection.IsUnknownContact(MapVisualPosture.Unknown, false), "An unidentified contact with unknown allegiance must be eligible.");
        foreach (var posture in new[] { MapVisualPosture.Allied, MapVisualPosture.Enemy, MapVisualPosture.Neutral })
            Program.Require(!ContactSelection.IsUnknownContact(posture, false), "Known allegiance must be excluded.");
        Program.Require(!ContactSelection.IsUnknownContact(MapVisualPosture.Unknown, true), "Already identified contact must be excluded.");
        var contacts = new[] { (Id: 1, Eligible: false), (Id: 2, Eligible: true), (Id: 1, Eligible: true), (Id: 2, Eligible: true) }.ToList();
        var snapshot = ContactSelection.Snapshot(contacts, contact => contact.Eligible, contact => contact.Id);
        contacts.Clear();
        Program.Require(snapshot.Select(contact => contact.Id).SequenceEqual(new[] { 2, 1 }), "Snapshot must preserve eligible order and process each track once even if the live collection changes.");
        Program.Require(ReferenceEquals(ModText.ForLanguage("ja"), ModText.Japanese), "Japanese game settings must select Japanese.");
        foreach (string? language in new[] { "en", "fr", "de", "cn", "ko", "es", "unknown", "", null })
            Program.Require(ReferenceEquals(ModText.ForLanguage(language), ModText.English), "English and unsupported languages must use English.");
        SourceScoping();
        MenuOrdering();
        Activation();
        Console.WriteLine("PASS target filtering, snapshot/deduplication, sender isolation/restoration, menu ordering and activation.");
    }

    private static void SourceScoping()
    {
        var source = new object(); var otherSource = new object(); var target = new object(); var otherTarget = new object();
        Program.Require(RequestSource<object, object>.For(target) == null, "Ordinary requests must have no override.");
        RequestSource<object, object>.Run(source, target, () =>
        {
            Program.Require(ReferenceEquals(RequestSource<object, object>.For(target), source), "The clicked sender must apply to its recipient.");
            Program.Require(RequestSource<object, object>.For(otherTarget) == null, "Unrelated requests must not inherit the override.");
            Program.Throws<InvalidOperationException>(() => RequestSource<object, object>.Run(otherSource, otherTarget, () =>
            {
                Program.Require(ReferenceEquals(RequestSource<object, object>.For(otherTarget), otherSource), "Nested request sender must be scoped.");
                throw new InvalidOperationException("Expected request failure.");
            }));
            Program.Require(ReferenceEquals(RequestSource<object, object>.For(target), source), "Failure must restore the outer sender.");
            object? crossThreadSender = source;
            var thread = new Thread(() => crossThreadSender = RequestSource<object, object>.For(target));
            thread.Start(); thread.Join();
            Program.Require(crossThreadSender == null, "The override must not cross threads.");
        });
        Program.Require(RequestSource<object, object>.For(target) == null, "The sender override must end with the request.");
        Program.Throws<InvalidOperationException>(() => RequestSource<object, object>.Run(source, target, () => throw new InvalidOperationException()));
        Program.Require(RequestSource<object, object>.For(target) == null, "Top-level failure must restore ordinary sender lookup.");
    }

    private static void MenuOrdering()
    {
        var identify = new ContextMenuItem("identify");
        var rebase = new ContextMenuItem("rebase");
        var children = new TrulyObservableCollection<ContextMenuItem> { identify, rebase };
        var orders = new ContextMenuItem("orders", children);
        var info = new ContextMenuItem("info");
        var root = new TrulyObservableCollection<ContextMenuItem> { info, orders };
        var added = new ContextMenuItem("bulk");
        MenuExtension.Insert(root, "orders", added);
        Program.Require(root.SequenceEqual(new[] { info, orders }) && children.SequenceEqual(new[] { added, identify, rebase }), "The new action must appear first in Orders without changing existing entries or their order.");

        children.Remove(added);
        children.Remove(identify);
        MenuExtension.Insert(root, "orders", added);
        Program.Require(children.SequenceEqual(new[] { added, rebase }), "An Orders menu without Identify must still receive the new action first.");

        var withoutOrders = new TrulyObservableCollection<ContextMenuItem> { info };
        MenuExtension.Insert(withoutOrders, "orders", added);
        Program.Require(withoutOrders.SequenceEqual(new[] { info }) && children.SequenceEqual(new[] { added, rebase }), "A menu without Orders must be skipped without modifying any menu.");
    }

    public sealed class Entry
    {
        public DirectoryInfo DirectoryInfo { get; set; } = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "BulkIdentificationRequestsTestPackage"));
        public bool IsChecked { get; set; }
    }
    public sealed class Manager
    {
        public static Manager Instance { get; } = new Manager();
        public Entry[] Directories { get; set; } = Array.Empty<Entry>();
    }
    private static void Activation()
    {
        var entry = new Entry();
        Manager.Instance.Directories = new[] { entry };
        Program.Require(!ModActivation.IsEnabled(typeof(Manager), entry.DirectoryInfo.FullName), "An unchecked package must install no patches.");
        entry.IsChecked = true;
        Program.Require(ModActivation.IsEnabled(typeof(Manager), entry.DirectoryInfo.FullName), "The checked package must be enabled.");
        Program.Throws<InvalidOperationException>(() => ModActivation.IsEnabled(typeof(Manager), entry.DirectoryInfo.FullName + "_other"));
    }
}
