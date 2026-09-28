using System.Globalization;
using System.Text.RegularExpressions;

namespace Offload.Crm.Web.Localization;

/// <summary>
/// D-29: UI texts in English or Slovak. The English text is the key; <see cref="Slovak"/> maps it to Slovak.
/// A text without a translation falls back to English (never blank). Messages that come from the services
/// (validation, conflicts) are translated the same way; entries with <c>{0}</c> placeholders also match
/// such messages with concrete values ("Customer has 3 projects…").
/// Usage in components: <c>@T("Customers")</c>, <c>T("User \"{0}\" created.", name)</c>.
/// </summary>
public static partial class Text
{
    private static readonly IReadOnlyList<(Regex Pattern, string Slovak)> Templates = Slovak.Texts
        .Where(e => e.Key.Contains("{0}", StringComparison.Ordinal))
        .Select(e => (TemplateRegex(e.Key), e.Value))
        .ToList();

    /// <summary>The current UI language (from the request culture: cookie, then browser).</summary>
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sk" ? "sk" : "en";

    public static string T(string? english)
    {
        if (string.IsNullOrEmpty(english) || Language != "sk")
        {
            return english ?? string.Empty;
        }

        if (Slovak.Texts.TryGetValue(english, out var slovak))
        {
            return slovak;
        }

        foreach (var (pattern, template) in Templates)
        {
            var match = pattern.Match(english);
            if (match.Success)
            {
                return string.Format(CultureInfo.CurrentCulture, template, match.Groups.Values.Skip(1).Select(g => (object)g.Value).ToArray());
            }
        }

        return english;
    }

    /// <summary>
    /// For user content that may hold a system-generated text (activity subjects/descriptions, timeline):
    /// translates only the texts the app itself writes ("Status changed: A → B", "Task completed: X", ...),
    /// never free text that happens to look like another UI string.
    /// </summary>
    public static string Generated(string? text)
    {
        if (string.IsNullOrEmpty(text) || Language != "sk")
        {
            return text ?? string.Empty;
        }

        foreach (var (pattern, template) in GeneratedTemplates)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                return string.Format(CultureInfo.CurrentCulture, template, match.Groups.Values.Skip(1).Select(g => (object)g.Value).ToArray());
            }
        }

        return text;
    }

    private static readonly IReadOnlyList<(Regex Pattern, string Slovak)> GeneratedTemplates = Slovak.Generated
        .Select(e => (TemplateRegex(e.Key), e.Value))
        .ToList();

    /// <summary>Translates <paramref name="template"/> and fills in the values (formatted with the app culture).</summary>
    public static string T(string template, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(template), args);

    /// <summary>
    /// Counted text with the right plural: English one/other, Slovak 1 / 2–4 / 0 and 5+ ("1 deň", "3 dni",
    /// "5 dní"). Each form may contain <c>{0}</c> for the number.
    /// </summary>
    public static string Plural(int n, string enOne, string enMany, string skOne, string skFew, string skMany)
    {
        var form = Language != "sk"
            ? (n == 1 ? enOne : enMany)
            : n == 1 ? skOne : n is >= 2 and <= 4 ? skFew : skMany;
        return string.Format(CultureInfo.CurrentCulture, form, n);
    }

    /// <summary>Translates every message of a field-error dictionary (validation from the services).</summary>
    public static Dictionary<string, string[]> T(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value.Select(m => T(m)).ToArray());

    /// <summary>"{0} projects" → <c>^(.+?) projects$</c> (placeholders capture any text).</summary>
    private static Regex TemplateRegex(string template) =>
        new("^" + PlaceholderRegex().Replace(Regex.Escape(template), "(.+?)") + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\\\{\d+}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>Supported UI languages (D-29). Number/date formatting is always sk-SK and EUR (D-28).</summary>
public static class AppLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<(string Code, string Name)> All = [("en", "English"), ("sk", "Slovenčina")];

    public static string Normalize(string? code) => All.Any(l => l.Code == code) ? code! : Default;
}
