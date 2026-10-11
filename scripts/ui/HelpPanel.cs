using Godot;
using SimulationCore;
using System;
using System.Linq;

namespace Sim2;

/// <summary>
/// Help browser: search field, result list, and one article at a time with
/// clickable related links. Content comes from the central <see cref="HelpCatalog"/>
/// in the core, so coverage is guaranteed by tests. Topics are reachable from
/// context buttons (e.g. the "?" in the genome editor).
/// </summary>
public partial class HelpPanel : VBoxContainer
{
    private readonly Main _main;
    private LineEdit? _search;
    private VBoxContainer? _results;
    private VBoxContainer? _article;
    private Label? _noResults;
    private string _currentKey = "";

    internal string CurrentKey => _currentKey;

    /// <summary>True after showing a fallback for a missing key.</summary>
    internal bool PlaceholderShown { get; private set; }

    internal HelpArticle CurrentArticle { get; private set; } = null!;

    public HelpPanel(Main main)
    {
        _main = main;
        Build();
    }

    private void Build()
    {
        AddChild(Ui.Title("Справочник"));

        var searchRow = new HBoxContainer();
        _search = new LineEdit
        {
            PlaceholderText = "Поиск: например, апоптоз, градиент, diffusion",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _search.TextChanged += _ => RebuildResults(_search!.Text);
        searchRow.AddChild(_search);
        searchRow.AddChild(Ui.Btn("Сброс", () => { _search.Text = ""; ShowArticle(_currentKey); }, 60));
        AddChild(searchRow);

        _results = new VBoxContainer();
        var resultsScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 120),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        resultsScroll.AddChild(_results);
        _noResults = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 0.85f, 0.4f)
        };
        resultsScroll.AddChild(_noResults);
        AddChild(resultsScroll);

        _article = new VBoxContainer();
        var articleScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        articleScroll.AddChild(_article);
        AddChild(articleScroll);

        ShowArticle("concept.gene_regulation");
    }

    public void ShowKey(string key)
    {
        _search!.Text = "";
        ShowArticle(key);
    }

    private void RebuildResults(string query)
    {
        if (_results == null || _noResults == null) return;
        Ui.Clear(_results);
        if (string.IsNullOrWhiteSpace(query))
        {
            _noResults.Visible = false;
            return;
        }
        var hits = HelpCatalog.Search(query).Take(40).ToList();
        if (hits.Count == 0)
        {
            _noResults.Text = $"Ничего не найдено по «{query}». Попробуйте другое слово.";
            _noResults.Visible = true;
            return;
        }
        _noResults.Visible = false;
        foreach (var article in hits)
        {
            _results.AddChild(Ui.Btn($"{article.Title}  —  {article.Summary}", () => ShowArticle(article.Key)));
        }
    }

    private void ShowArticle(string key)
    {
        _currentKey = key;
        if (_article == null) return;
        Ui.Clear(_article);

        var article = HelpCatalog.Get(key);
        if (article == null)
        {
            PlaceholderShown = true;
            GD.PushWarning($"[help] статья «{key}» не найдена в каталоге — показан запасной вариант.");
            article = HelpCatalog.GetOrFallback(key, "Справочная статья");
            _article.AddChild(new Label
            {
                Text = $"Статья «{key}» пока не написана. Показано запасное пояснение.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(1f, 0.7f, 0.3f)
            });
        }
        CurrentArticle = article;
        if (!article.IsPlaceholder) PlaceholderShown = false;
        var title = Ui.Title(article.Title);
        title.TooltipText = article.Key;
        _article.AddChild(title);

        if (article.Summary.Length > 0) _article.AddChild(Ui.Lbl(article.Summary, 0, 0, wrap: true));
        if (article.Where.Length > 0) _article.AddChild(Section("Где настраивается", article.Where));
        if (article.Effect.Length > 0) _article.AddChild(Section("Что делает", article.Effect));
        if (article.Range.Length > 0) _article.AddChild(Section("Диапазоны значений", article.Range));
        if (article.UpDown.Length > 0) _article.AddChild(Section("Как меняется поведение", article.UpDown));
        if (article.Experiment.Length > 0) _article.AddChild(Section("Эксперимент", article.Experiment));

        var related = HelpCatalog.RelatedKeys(article).Take(12).ToList();
        if (related.Count > 0)
        {
            _article.AddChild(Ui.Title("См. также"));
            foreach (var relKey in related)
            {
                var r = HelpCatalog.Get(relKey);
                if (r == null) continue;
                _article.AddChild(Ui.Btn($"→ {r.Title}", () => ShowArticle(relKey)));
            }
        }
    }

    private static Control Section(string title, string text)
    {
        var box = new VBoxContainer();
        box.AddChild(Ui.Title(title));
        box.AddChild(Ui.Lbl(text, 0, 0, wrap: true));
        return box;
    }
}