using SeaPower;

namespace BulkIdentificationRequests;

internal sealed class ModText
{
    internal string Label { get; }
    internal string Tooltip { get; }

    internal ModText(LanguageResourceHandler language)
    {
        Label = language.getText("BulkIdentificationRequests", "RequestAllLabel", true, true);
        Tooltip = language.getText("BulkIdentificationRequests", "RequestAllTooltip", true, true);
    }
}
