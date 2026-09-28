using System.Globalization;
using System.Text.RegularExpressions;
using Offload.Crm.Web.Localization;

namespace Offload.Crm.Tests.Unit;

/// <summary>D-29: English/Slovak UI texts.</summary>
public sealed partial class LocalizationTests : IDisposable
{
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _uiCulture;

    private static void Slovak() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sk");

    private static void English() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void English_is_returned_unchanged()
    {
        English();

        Assert.Equal("Customers", Text.T("Customers"));
        Assert.Equal("Customer \"ACME\" deleted.", Text.T("Customer \"{0}\" deleted.", "ACME"));
    }

    [Fact]
    public void Slovak_translates_exact_texts_and_messages_with_values()
    {
        Slovak();

        Assert.Equal("Zákazníci", Text.T("Customers"));
        Assert.Equal("Zákazník „ACME“ bol vymazaný.", Text.T("Customer \"{0}\" deleted.", "ACME"));

        // A finished English message from a service (values already inside) is matched against the templates.
        Assert.Equal("Zákazník „ACME“ bol vymazaný.", Text.T("Customer \"ACME\" deleted."));
        Assert.Equal("Zákazník má projekty (3). Najprv ich vymažte alebo priraďte inému zákazníkovi.", Text.T("Customer has 3 projects. Delete or reassign them first."));

        // Unknown texts fall back to English, never blank.
        Assert.Equal("Something nobody translated", Text.T("Something nobody translated"));
        Assert.Equal(string.Empty, Text.T((string?)null));
    }

    [Theory]
    [InlineData(1, "o 1 deň", "in 1 day")]
    [InlineData(3, "o 3 dni", "in 3 days")]
    [InlineData(5, "o 5 dní", "in 5 days")]
    [InlineData(0, "o 0 dní", "in 0 days")]
    public void Plural_uses_slovak_one_few_many(int n, string slovak, string english)
    {
        Slovak();
        Assert.Equal(slovak, Text.Plural(n, "in {0} day", "in {0} days", "o {0} deň", "o {0} dni", "o {0} dní"));

        English();
        Assert.Equal(english, Text.Plural(n, "in {0} day", "in {0} days", "o {0} deň", "o {0} dni", "o {0} dní"));
    }

    [Fact]
    public void Generated_translates_system_texts_but_leaves_user_text_alone()
    {
        Slovak();

        Assert.Equal("Zmena stavu: Lead → Analysis", Text.Generated("Status changed: Lead → Analysis"));
        Assert.Equal("Dokončená úloha: Kickoff", Text.Generated("Task completed: Kickoff"));
        Assert.Equal("Telefonát", Text.Generated("Phone call"));

        // Free text that merely resembles a UI string ("{0} days ago") must not be rewritten.
        Assert.Equal("Called the client, he paid 5 days ago", Text.Generated("Called the client, he paid 5 days ago"));
        Assert.Equal("Done", Text.Generated("Done"));
    }

    [Fact]
    public void Language_normalization_only_accepts_supported_languages()
    {
        Assert.Equal("sk", AppLanguages.Normalize("sk"));
        Assert.Equal("en", AppLanguages.Normalize("de"));
        Assert.Equal("en", AppLanguages.Normalize(null));
    }

    [Fact]
    public void Slovak_texts_use_only_placeholders_of_the_english_key()
    {
        var problems = Offload.Crm.Web.Localization.Slovak.Texts
            .Concat(Offload.Crm.Web.Localization.Slovak.Generated)
            .Where(e => !Placeholders(e.Value).IsSubsetOf(Placeholders(e.Key)) || string.IsNullOrWhiteSpace(e.Value))
            .Select(e => $"{e.Key} => {e.Value}")
            .ToList();

        Assert.Empty(problems);
    }

    /// <summary>Every UI text in the Web and Application source has a Slovak translation (no English leftovers).</summary>
    [Fact]
    public void Every_ui_text_in_the_source_has_a_slovak_translation()
    {
        var root = RepositoryRoot();
        var files = new[] { "src/Offload.Crm.Web", "src/Offload.Crm.Application" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Api{Path.DirectorySeparatorChar}", StringComparison.Ordinal)); // the API stays English

        var missing = files
            .SelectMany(f => UiTexts(File.ReadAllText(f)).Select(text => (File: Path.GetFileName(f), Text: text)))
            .Where(x => !Offload.Crm.Web.Localization.Slovak.Texts.ContainsKey(x.Text))
            .Select(x => $"{x.File}: {x.Text}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(missing.Count == 0, "Missing Slovak translations:\n" + string.Join("\n", missing));
    }

    private static IEnumerable<string> UiTexts(string source)
    {
        const string Str = """(\$?)"((?:[^"\\]|\\.)*)" """;
        var patterns = new[]
        {
            $@"\bT\(\s*{Str.Trim()}",
            $@"[Ss]nackbar\.Add\(\s*{Str.Trim()}",
            $@"ConfirmDeleteAsync\(\s*{Str.Trim()}",
            $@"ErrorMessage\s*=\s*{Str.Trim()}",
            $@"new (?:Conflict|Forbidden|NotFound)Exception\(\s*{Str.Trim()}",
            $@"(?:public|private) const string \w*(?:Message|Hint)\w*\s*=\s*{Str.Trim()}",
        };
        foreach (var pattern in patterns)
        {
            foreach (Match m in Regex.Matches(source, pattern))
            {
                if (Normalize(m.Groups[2].Value, m.Groups[1].Value == "$") is { } text)
                {
                    yield return text;
                }
            }
        }

        // T(cond ? "A" : "B") and snackbar ternaries
        foreach (Match m in TernaryRegex().Matches(source))
        {
            foreach (var group in new[] { m.Groups[1].Value, m.Groups[2].Value })
            {
                if (Normalize(group, interpolated: false) is { } text)
                {
                    yield return text;
                }
            }
        }
    }

    /// <summary>$"a {x} b {y}" → "a {0} b {1}"; skips identifiers/test ids.</summary>
    private static string? Normalize(string value, bool interpolated)
    {
        if (interpolated)
        {
            var i = 0;
            value = InterpolationRegex().Replace(value, _ => "{" + i++ + "}");
        }

        value = value.Replace("\\\"", "\"", StringComparison.Ordinal);
        return LetterRegex().IsMatch(value) && !IdentifierRegex().IsMatch(value) ? value : null;
    }

    private static HashSet<string> Placeholders(string text) => PlaceholderRegex().Matches(text).Select(m => m.Value).ToHashSet();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Offload.Crm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Offload.Crm.sln not found above the test output directory.");
    }

    [GeneratedRegex(@"\b(?:T|[Ss]nackbar\.Add)\([^()""]*?\?\s*""((?:[^""\\]|\\.)*)""\s*:\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex TernaryRegex();

    [GeneratedRegex(@"\{(?!\d+\})[^{}]*\}")]
    private static partial Regex InterpolationRegex();

    [GeneratedRegex("[A-Za-z]{2}")]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"^(?:[a-z]+(?:-[a-z]+)+|#\w+|https?:.*)$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderRegex();
}
