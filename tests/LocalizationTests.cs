using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using BulkIdentificationRequests;
using SeaPower;

internal static class LocalizationTests
{
    private const string Section = "BulkIdentificationRequests";

    internal static void Run()
    {
        var english = LoadLanguage("en");
        var japanese = LoadLanguage("ja");
        var englishText = new ModText(CreateHandler(english, null));
        var japaneseText = new ModText(CreateHandler(japanese, english));

        Program.Require(englishText.Label == "Request ID from all unknowns", "The English language file must supply the published menu label.");
        Program.Require(japaneseText.Label == "全不明接触の身元を求める", "The Japanese language file must supply the published menu label.");
        Program.Require(!string.IsNullOrWhiteSpace(englishText.Tooltip) &&
            !string.IsNullOrWhiteSpace(japaneseText.Tooltip) && englishText.Tooltip != japaneseText.Tooltip,
            "Both language files must supply their own tooltip.");

        var unsupportedText = new ModText(CreateHandler(new Dictionary<string, string>(), english));
        Program.Require(unsupportedText.Label == englishText.Label && unsupportedText.Tooltip == englishText.Tooltip,
            "The game's fallback dictionary must supply English when the selected language has no translations.");

        var partial = new Dictionary<string, string>(japanese);
        partial.Remove(Key("RequestAllTooltip"));
        var partialText = new ModText(CreateHandler(partial, english));
        Program.Require(partialText.Label == japaneseText.Label && partialText.Tooltip == englishText.Tooltip,
            "A missing individual translation must fall back to English without losing the available Japanese label.");

        var replacement = new Dictionary<string, string>(japanese)
        {
            [Key("RequestAllLabel")] = "変更された翻訳",
            [Key("RequestAllTooltip")] = "変更された説明"
        };
        var replacementHandler = CreateHandler(replacement, english);
        var replacementText = new ModText(replacementHandler);
        Program.Require(replacementText.Label == "変更された翻訳" && replacementText.Tooltip == "変更された説明",
            "Replacing the language handler and its dictionary must supply the new translations.");

        for (int index = 0; index < 20; index++)
        {
            var current = index % 2 == 0 ? englishText : japaneseText;
            var text = new ModText(CreateHandler(index % 2 == 0 ? english : japanese, english));
            Program.Require(text.Label == current.Label && text.Tooltip == current.Tooltip,
                "Successive handlers must not inherit another handler's translations.");
        }

        replacement[Key("RequestAllLabel")] = "再読み込みされた翻訳";
        Program.Require(new ModText(replacementHandler).Label == "再読み込みされた翻訳",
            "A fresh menu must read the handler's current dictionary instead of a cached text.");

        Console.WriteLine("PASS game INI parsing, English fallback per key, dictionary overrides and language-handler replacement.");
    }

    private static string Key(string key) => (Section + key).ToLowerInvariant();

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "language_" + language, "bulk_identification_requests.ini");
        using var input = new StreamReader(path);
        var parser = new IniParser(input, path);
        using var tokens = parser.Lex();
        using var regions = parser.Parse(tokens);
        var result = new Dictionary<string, string>();
        while (regions.MoveNext())
        {
            var region = regions.Current;
            using var values = region.Values;
            while (values.MoveNext())
                result.Add((region.Name + values.Current.Key).ToLowerInvariant(), values.Current.Value);
        }
        return result;
    }

    private static LanguageResourceHandler CreateHandler(Dictionary<string, string> primary,
        Dictionary<string, string>? fallback)
    {
        // Supply dictionary state without invoking Unity's scene-object constructors.
        // The production text lookup and the game's fallback dictionary run unchanged.
#pragma warning disable SYSLIB0050
        var handler = (LanguageResourceHandler)FormatterServices.GetUninitializedObject(typeof(LanguageResourceHandler));
#pragma warning restore SYSLIB0050
        var keys = typeof(LanguageResourceHandler).GetField("keyDictionary", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(LanguageResourceHandler).FullName, "keyDictionary");
        keys.SetValue(handler, new ConcurrentDictionary<(string, string), string>());
        var dictionary = typeof(LanguageResourceHandler).GetProperty(nameof(LanguageResourceHandler.LanguageDictionary))!;
        var value = Activator.CreateInstance(dictionary.PropertyType, new object?[] { "MISSING TEXT", primary, fallback });
        dictionary.SetValue(handler, value);
        return handler;
    }
}
