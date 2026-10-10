namespace SimulationCore;

/// <summary>
/// A single reference article. Every field is optional except the key/title;
/// the UI shows only the non-empty sections. This is pure data kept separate
/// from the simulation logic so it can be tested and translated independently.
/// </summary>
public sealed class HelpArticle
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>What this is (simple explanation).</summary>
    public string Summary { get; init; } = "";
    /// <summary>Where it is applied.</summary>
    public string Where { get; init; } = "";
    /// <summary>How it changes simulation behaviour.</summary>
    public string Effect { get; init; } = "";
    /// <summary>Allowed values and units.</summary>
    public string Range { get; init; } = "";
    /// <summary>What happens when the value is increased / decreased.</summary>
    public string UpDown { get; init; } = "";
    /// <summary>Related parameters/mechanics.</summary>
    public string Related { get; init; } = "";
    /// <summary>A small practical experiment to study this parameter.</summary>
    public string Experiment { get; init; } = "";
    /// <summary>Extra keywords for search (identifiers, synonyms).</summary>
    public string[] Keywords { get; init; } = Array.Empty<string>();
    /// <summary>True when this is an auto-generated placeholder article.</summary>
    public bool IsPlaceholder { get; init; }
}

/// <summary>
/// Centralized Russian reference ("справочник") for regulators, operators,
/// logic, effects, timers, morphogens, concepts and editable parameters. Lookup
/// by stable key; contextual help buttons resolve keys such as
/// <c>effect.EmitMorphogen</c> or <c>param.morphogenDiffusion</c>.
/// </summary>
public static partial class HelpCatalog
{
    private static readonly Dictionary<string, HelpArticle> ByKey;

    static HelpCatalog()
    {
        ByKey = new Dictionary<string, HelpArticle>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in BuildArticles()) ByKey[a.Key] = a;
    }

    public static IReadOnlyCollection<HelpArticle> Articles => ByKey.Values;

    /// <summary>Returns the article for a key, or null when it does not exist.</summary>
    public static HelpArticle? Get(string key)
        => !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out var a) ? a : null;

    /// <summary>
    /// Returns the article for a key, or a clearly-marked placeholder when the
    /// specific article is missing (so help links never dead-end).
    /// </summary>
    public static HelpArticle GetOrFallback(string key, string? context = null)
    {
        var a = Get(key);
        if (a != null) return a;
        string title = string.IsNullOrWhiteSpace(context) ? $"Пояснение «{key}»" : context!;
        return new HelpArticle
        {
            Key = key,
            Title = title,
            Summary = "Для этого параметра подробное описание пока не подготовлено.",
            Where = context ?? "",
            Effect = "Опирайтесь на соседние статьи справочника; механизм соответствует общей модели Sim2.",
            IsPlaceholder = true
        };
    }

    /// <summary>Searches by Russian title, technical identifier and keywords.</summary>
    public static IEnumerable<HelpArticle> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Articles;
        string q = query.Trim();
        return Articles.Where(a =>
            a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Key.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Splits an article's <see cref="HelpArticle.Related"/> field into link
    /// tokens. Tokens containing a dot (e.g. <c>effect.Grow</c>) are always
    /// treated as links; other tokens are treated as links only when they resolve
    /// to an article (so free-text class names are ignored).
    /// </summary>
    public static IReadOnlyList<string> RelatedKeys(HelpArticle article)
    {
        var result = new List<string>();
        foreach (var raw in article.Related.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Contains('.') || Get(raw) != null) result.Add(raw);
        }
        return result;
    }

    // Convenience builders so the article table stays readable.
    private static HelpArticle A(
        string key, string title, string summary, string where, string effect,
        string range, string upDown, string related, string experiment,
        params string[] keywords)
        => new()
        {
            Key = key, Title = title, Summary = summary, Where = where, Effect = effect,
            Range = range, UpDown = upDown, Related = related, Experiment = experiment,
            Keywords = keywords
        };
}
