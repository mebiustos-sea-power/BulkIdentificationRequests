using System;
using System.Linq;
using SeaPower;
using SeapowerUI;

namespace BulkIdentificationRequests;

internal static class BulkRequests
{
    internal static bool IsPlayerUnit(ObjectBase sender) => sender != null && !sender.IsDestroyed && sender.isUnit() &&
        ReferenceEquals(sender._taskforce, Globals._playerTaskforce);

    internal static bool CanSend(ObjectBase sender) => IsPlayerUnit(sender) && !sender.isSubmerged();

    internal static bool Eligible(MapUnitViewModel contact, Taskforce player) =>
        ReferenceEquals(contact.OurTaskforce, player) && ContactSelection.IsUnknownShip(
            contact.VisualType, contact.VisualPosture, contact.Unit.Identified.Value);

    internal static void Execute(ObjectBase sender)
    {
        try
        {
            if (!VanillaRequests.Ready) throw new InvalidOperationException("Bulk identification requests are unavailable.");
            if (!CanSend(sender)) return;
            var player = Globals._playerTaskforce ?? throw new InvalidOperationException("The player task force is unavailable.");
            var model = Globals._mainGameViewModel ?? throw new InvalidOperationException("The battle view is unavailable.");
            var known = model.Map?.MapKnownUnits?.AllUnits
                ?? throw new InvalidOperationException("The player's known contacts are unavailable.");
            var targets = ContactSelection.Snapshot(known, contact => Eligible(contact, player), contact => contact.Unit.Id);
            int attempted = 0;
            foreach (var target in targets)
            {
                if (!CanSend(sender) || !ReferenceEquals(player, Globals._playerTaskforce) ||
                    !ReferenceEquals(model, Globals._mainGameViewModel)) break;
                if (!known.Contains(target) || !Eligible(target, player)) continue;
                VanillaRequests.Send(sender, target);
                attempted++;
            }
            ModLog.Info("Executed " + attempted + " standard identification request(s). Responses are reported by the game.");
        }
        catch (Exception error) { ModLog.Error("Bulk identification stopped", error); }
    }
}
