using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GarageStack.Tests;

/// <summary>
/// Every translation of a resource set has the same keys and the same format items as its English
/// file. A missing key raises no error at run time: the localizer quietly falls back to the parent
/// culture, so a notification would go out in Brazilian Portuguese or English instead. A missing
/// or extra format item makes string.Format throw, or drops a value, when the text is filled in.
/// The frontend's locale files have the same guard in the type check.
/// </summary>
public partial class ResourceTranslationTests
{
    private static readonly string[] ResourceFolders =
    [
        "src/GarageStack.Api/Resources",
        "src/GarageStack.Worker/Resources",
    ];

    // {0}, {1:N1} and {0,5} are one format item each, counted by their index. {{ is a literal brace.
    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex FormatItem();

    /// <summary>Each English resource file: Name.resx, without a culture in its name.</summary>
    public static TheoryData<string> ResourceSets()
    {
        var sets = new TheoryData<string>();
        foreach (var folder in ResourceFolders)
            foreach (var file in Directory.GetFiles(RepositoryFiles.PathOf(folder), "*.resx").Where(f => Culture(f) is null).Order(StringComparer.Ordinal))
                sets.Add($"{folder}/{Path.GetFileName(file)}");
        return sets;
    }

    [Fact]
    public void ResourceSets_AreFound()
    {
        Assert.Contains("src/GarageStack.Worker/Resources/NotificationStrings.resx", ResourceSets().Select(row => row.Data));
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void EveryTranslation_HasEveryKey_WithTheSameFormatItems(string englishFile)
    {
        var english = Read(RepositoryFiles.PathOf(englishFile));
        var translations = TranslationsOf(RepositoryFiles.PathOf(englishFile));
        Assert.NotEmpty(translations);

        var problems = new List<string>();
        foreach (var (culture, path) in translations)
        {
            var translated = Read(path);
            problems.AddRange(english.Keys.Except(translated.Keys).Order(StringComparer.Ordinal).Select(key => $"{culture}: missing {key}"));
            problems.AddRange(translated.Keys.Except(english.Keys).Order(StringComparer.Ordinal).Select(key => $"{culture}: {key} is not in the English file"));
            problems.AddRange(english.Keys.Intersect(translated.Keys).Order(StringComparer.Ordinal)
                .Where(key => !FormatItems(english[key]).SequenceEqual(FormatItems(translated[key])))
                .Select(key => $"{culture}: {key} has format items {string.Join(",", FormatItems(translated[key]))}, English has {string.Join(",", FormatItems(english[key]))}"));
            problems.AddRange(translated.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => $"{culture}: {entry.Key} is empty"));
        }

        Assert.True(problems.Count == 0, $"{englishFile}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // A language that one resource set is translated into needs every other set as well, or its
    // users get, say, Portuguese notifications next to an English widget.
    [Fact]
    public void EveryResourceSet_IsTranslatedIntoTheSameLanguages()
    {
        var cultures = ResourceSets()
            .Select(row => row.Data)
            .ToDictionary(file => file, file => TranslationsOf(RepositoryFiles.PathOf(file)).Keys.Order(StringComparer.Ordinal).ToArray());
        var all = cultures.Values.SelectMany(c => c).Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.All(cultures, set => Assert.True(
            set.Value.SequenceEqual(all),
            $"{set.Key} is translated into {string.Join(", ", set.Value)}, but the resource sets together into {string.Join(", ", all)}"));
    }

    private static Dictionary<string, string> TranslationsOf(string englishPath)
    {
        var name = Path.GetFileNameWithoutExtension(englishPath);
        return Directory.GetFiles(Path.GetDirectoryName(englishPath)!, $"{name}.*.resx")
            .Where(file => Path.GetFileNameWithoutExtension(file).StartsWith(name + ".", StringComparison.Ordinal))
            .Where(file => Culture(file) is not null)
            .ToDictionary(file => Culture(file)!, file => file, StringComparer.Ordinal);
    }

    // "NotificationStrings.pt-PT.resx" is in pt-PT; "NotificationStrings.resx" in none.
    private static string? Culture(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? null : name[(dot + 1)..];
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "", StringComparer.Ordinal);

    private static int[] FormatItems(string text) =>
        [.. FormatItem().Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];
}
