using System;

namespace BulkIdentificationRequests;

// The override belongs to one synchronous request, not to the player's UI selection.
internal static class RequestSource<TSource, TTarget> where TSource : class where TTarget : class
{
    [ThreadStatic] private static TSource? source;
    [ThreadStatic] private static TTarget? target;

    internal static TSource? For(TTarget requestedTarget) => ReferenceEquals(target, requestedTarget) ? source : null;

    internal static void Run(TSource sender, TTarget recipient, Action action)
    {
        if (sender == null) throw new ArgumentNullException(nameof(sender));
        if (recipient == null) throw new ArgumentNullException(nameof(recipient));
        var previousSource = source;
        var previousTarget = target;
        source = sender;
        target = recipient;
        try { action(); }
        finally { source = previousSource; target = previousTarget; }
    }
}
