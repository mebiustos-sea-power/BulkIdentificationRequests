using System;

namespace BulkIdentificationRequests;

internal sealed class ModText
{
    internal string Label { get; }
    internal string Tooltip { get; }
    private ModText(string label, string tooltip) { Label = label; Tooltip = tooltip; }

    internal static readonly ModText English = new ModText(
        "Request identification from all unknown ships",
        "Send the standard identification request once to each known unknown surface contact from this unit. Normal communication limits and exposure of the sender apply.");
    internal static readonly ModText Japanese = new ModText(
        "全不明艦の身元を求める",
        "このユニットから、探知済みの不明艦へ通常の身元確認要求を１回ずつ送ります。通信制限と発信元の露見は標準処理に従います。");

    internal static ModText ForLanguage(string? language) =>
        string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase) ? Japanese : English;
}
