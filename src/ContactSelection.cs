using System;
using System.Collections.Generic;
using SeapowerUI;

namespace BulkIdentificationRequests;

internal static class ContactSelection
{
    internal static bool IsUnknownContact(MapVisualPosture posture, bool identified) =>
        posture == MapVisualPosture.Unknown && !identified;

    // Snapshot and deduplicate before requests can update the game's observable map collection.
    internal static List<T> Snapshot<T, TKey>(IEnumerable<T> knownContacts, Func<T, bool> eligible, Func<T, TKey> key)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>();
        var result = new List<T>();
        foreach (var contact in knownContacts)
            if (eligible(contact) && seen.Add(key(contact))) result.Add(contact);
        return result;
    }
}
