using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AODB.Common.RDBObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit front end for the GFX Test scene (<see cref="GfxTest_DEV"/>): database, spawn controls,
/// and virtualised lists of the gfxtweak effects and of the nanos that use them. Every row says how
/// much of it the port reproduces (<see cref="EffectCoverage"/>), so it is clear what should already
/// look like the game. Replaces the old in-game IMGUI panel, whose ~2,700 layout buttons ran the
/// scene at about 10 fps.
/// </summary>
public sealed class GfxTestWindow : EditorWindow
{
    const int RefreshMs = 200;
    const float RowHeight = 26f;

    // Nano stats (NanoEffectResolver).
    const int StatCast = 428;
    const int StatTracer = 419;
    const int StatImpact = 414;
    const int StatHit = 361;
    const int StatBuff = 413; // effecttype: on the recipient while the buff runs

    struct EffectRow
    {
        public int Id;
        public int TypeCode;
        public EffectCategory Category;
        public string StockClass;
        public EffectStatus Status;
        public string Search;
    }

    struct NanoRow
    {
        public int Id;
        public string Name;
        public int[] Effects;           // cast, tracer, impact, hit, buff (0 = none)
        public EffectStatus?[] Statuses; // per role, null when the nano has none
        public EffectStatus Status;
        public string Search;
    }

    static readonly string[] RoleLetters = { "C", "T", "I", "H", "B" };
    static readonly string[] RoleNames = { "cast", "tracer", "impact", "hit", "buff" };

    // Category chips: a label and the categories it takes; an empty set is "All".
    static readonly (string Label, EffectCategory[] Categories)[] Categories =
    {
        ("All", Array.Empty<EffectCategory>()),
        ("Sprite", new[] { EffectCategory.Sprite }),
        ("Composite", new[] { EffectCategory.Composite }),
        ("Particle", new[] { EffectCategory.Particle }),
        ("Point cloud", new[] { EffectCategory.PointCloud }),
        ("Tracer", new[] { EffectCategory.Tracer }),
        ("Mesh", new[] { EffectCategory.Mesh }),
        ("Other", new[]
        {
            EffectCategory.Screen, EffectCategory.Audio, EffectCategory.Buff,
            EffectCategory.Unidentified, EffectCategory.None,
        }),
    };

    // Status chips: index 0 is "Any", then one per EffectStatus, best first.
    static readonly (string Label, EffectStatus? Status)[] Statuses =
    {
        ("Any", null),
        ("Verified", EffectStatus.Verified),
        ("Unverified", EffectStatus.Unverified),
        ("Approx", EffectStatus.Approximated),
        ("Missing", EffectStatus.Missing),
    };

    readonly List<EffectRow> _effects = new List<EffectRow>(4096);
    readonly List<EffectRow> _effectsShown = new List<EffectRow>(4096);
    readonly List<NanoRow> _nanos = new List<NanoRow>(16384);
    readonly List<NanoRow> _nanosShown = new List<NanoRow>(16384);
    readonly List<Button> _categoryChips = new List<Button>();
    readonly List<Button> _statusChips = new List<Button>();

    GfxTest_DEV _bound;
    EffectCoverage _coverage;
    int _catalogVersion = -1;
    int _nanoVersion = -1;
    bool _nanoMode;
    int _categoryFilter;
    int _statusFilter;
    string _search = string.Empty;
    int _fpsFrame;
    double _fpsTime;
    int _nanoInfoId = -1;

    VisualElement _content;
    VisualElement _empty;
    Label _emptyTitle;
    Label _emptyBody;
    Button _emptyAction;
    Label _fpsPill;
    Label _livePill;
    Label _dbPill;
    TextField _aoPath;
    IntegerField _effectField;
    IntegerField _nanoField;
    ColorField _tintField;
    Label _recordChip;
    Label _recordBadge;
    Label _recordText;
    VisualElement _nanoInfo;
    Label _nanoName;
    readonly Label[] _nanoRoles = new Label[RoleNames.Length];
    Button _clearButton;
    Button _effectsTab;
    Button _nanosTab;
    VisualElement _categoryRow;
    Label _countLabel;
    ListView _effectList;
    ListView _nanoList;
    Label _hint;
    Label _status;

    [MenuItem("Lost Eden/GFX Test")]
    public static void Open()
    {
        var window = GetWindow<GfxTestWindow>();
        window.titleContent = new GUIContent("GFX Test");
        window.minSize = new Vector2(380f, 560f);
    }

    void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        StyleSheet sheet = LoadStyleSheet();
        if (sheet != null)
            root.styleSheets.Add(sheet);
        root.AddToClassList("gfx-root");
        if (!EditorGUIUtility.isProSkin)
            root.AddToClassList("gfx-light");

        root.Add(BuildHeader());

        _empty = BuildEmptyState();
        root.Add(_empty);

        _content = El("gfx-content");
        _content.Add(BuildDatabaseCard());
        _content.Add(BuildSpawnCard());
        _content.Add(BuildBrowserCard());
        root.Add(_content);

        root.Add(BuildStatusBar());

        root.schedule.Execute(Refresh).Every(RefreshMs);
        Refresh();
    }

    // ---- Layout ----

    VisualElement BuildHeader()
    {
        VisualElement header = El("gfx-header");
        VisualElement titles = El("gfx-titles");
        titles.Add(Text("GFX Test", "gfx-title"));
        titles.Add(Text("gfxtweak catalog · nano cast bench", "gfx-subtitle"));
        header.Add(titles);
        header.Add(El("gfx-spacer"));
        _fpsPill = Text("—", "gfx-pill");
        _livePill = Text("—", "gfx-pill");
        _dbPill = Text("—", "gfx-pill");
        header.Add(_fpsPill);
        header.Add(_livePill);
        header.Add(_dbPill);
        return header;
    }

    VisualElement BuildEmptyState()
    {
        VisualElement empty = El("gfx-empty");
        _emptyTitle = Text(string.Empty, "gfx-empty-title");
        _emptyBody = Text(string.Empty, "gfx-empty-body");
        _emptyAction = Btn(string.Empty, OnEmptyAction, "gfx-primary", "gfx-empty-action");
        empty.Add(_emptyTitle);
        empty.Add(_emptyBody);
        empty.Add(_emptyAction);
        return empty;
    }

    VisualElement BuildDatabaseCard()
    {
        VisualElement card = Card("Database");

        VisualElement pathRow = El("gfx-row");
        _aoPath = new TextField();
        _aoPath.AddToClassList("gfx-grow");
        _aoPath.RegisterValueChangedCallback(e => With(t => t.AoPath = e.newValue));
        pathRow.Add(_aoPath);
        pathRow.Add(Btn("Browse…", BrowseAoPath));
        card.Add(pathRow);

        VisualElement actions = El("gfx-row");
        actions.Add(Btn("Open DB", () => With(t => t.OpenDatabase()), "gfx-grow"));
        actions.Add(Btn("Reload catalog", () => With(t => t.ReloadCatalog()), "gfx-grow"));
        card.Add(actions);
        return card;
    }

    VisualElement BuildSpawnCard()
    {
        VisualElement card = Card("Spawn");

        VisualElement fields = El("gfx-row", "gfx-fields");
        _effectField = new IntegerField("Effect");
        _effectField.AddToClassList("gfx-field");
        _effectField.RegisterValueChangedCallback(e =>
        {
            With(t => t.EffectId = e.newValue);
            UpdateRecordInfo();
            SelectEffect(e.newValue);
        });
        _nanoField = new IntegerField("Nano");
        _nanoField.AddToClassList("gfx-field");
        _nanoField.RegisterValueChangedCallback(e =>
        {
            With(t => t.NanoId = e.newValue);
            UpdateNanoInfo();
            SelectNano(e.newValue);
        });
        _tintField = new ColorField("Tint") { showAlpha = true, hdr = false };
        _tintField.AddToClassList("gfx-field");
        _tintField.RegisterValueChangedCallback(e => With(t => t.Tint = e.newValue));
        fields.Add(_effectField);
        fields.Add(_nanoField);
        fields.Add(_tintField);
        card.Add(fields);

        VisualElement record = El("gfx-record");
        _recordChip = Text(string.Empty, "gfx-chip");
        _recordBadge = Text(string.Empty, "gfx-badge");
        _recordText = Text(string.Empty, "gfx-record-text");
        record.Add(_recordChip);
        record.Add(_recordBadge);
        record.Add(_recordText);
        card.Add(record);

        _nanoInfo = El("gfx-record", "gfx-nano-info");
        _nanoName = Text(string.Empty, "gfx-record-text", "gfx-nano-name");
        _nanoInfo.Add(_nanoName);
        for (int i = 0; i < _nanoRoles.Length; i++)
        {
            _nanoRoles[i] = Text(RoleNames[i], "gfx-role");
            _nanoInfo.Add(_nanoRoles[i]);
        }
        card.Add(_nanoInfo);

        VisualElement buttons = El("gfx-row");
        buttons.Add(Btn("Spawn at look", () => With(t => t.SpawnAtLook()), "gfx-primary", "gfx-grow"));
        buttons.Add(Btn("At origin", () => With(t => t.SpawnAtOrigin()), "gfx-grow"));
        buttons.Add(Btn("Cast nano", () => With(t => t.SpawnFromNano()), "gfx-accent", "gfx-grow"));
        _clearButton = Btn("Clear", () => With(t => t.ClearEffects()), "gfx-danger");
        buttons.Add(_clearButton);
        card.Add(buttons);
        return card;
    }

    VisualElement BuildBrowserCard()
    {
        VisualElement card = El("gfx-card", "gfx-card--fill");

        VisualElement tabs = El("gfx-tabs");
        _effectsTab = Btn("Effects", () => SetMode(false), "gfx-tab");
        _nanosTab = Btn("Nanos", () => SetMode(true), "gfx-tab");
        tabs.Add(_effectsTab);
        tabs.Add(_nanosTab);
        tabs.Add(El("gfx-spacer"));
        _countLabel = Text(string.Empty, "gfx-count");
        tabs.Add(_countLabel);
        card.Add(tabs);

        var search = new ToolbarSearchField();
        search.AddToClassList("gfx-search");
        search.RegisterValueChangedCallback(e =>
        {
            _search = e.newValue ?? string.Empty;
            ApplyFilter();
        });
        card.Add(search);

        _categoryRow = El("gfx-chips");
        for (int i = 0; i < Categories.Length; i++)
        {
            int index = i;
            Button chip = Btn(Categories[i].Label, () => { _categoryFilter = index; ApplyFilter(); }, "gfx-filter");
            _categoryChips.Add(chip);
            _categoryRow.Add(chip);
        }
        card.Add(_categoryRow);

        VisualElement statusRow = El("gfx-chips");
        for (int i = 0; i < Statuses.Length; i++)
        {
            int index = i;
            string variant = Statuses[i].Status.HasValue ? StatusClass(Statuses[i].Status.Value) : null;
            Button chip = Btn(Statuses[i].Label, () => { _statusFilter = index; ApplyFilter(); }, "gfx-filter", "gfx-filter--status");
            if (variant != null)
                chip.AddToClassList(variant);
            _statusChips.Add(chip);
            statusRow.Add(chip);
        }
        card.Add(statusRow);

        _effectList = new ListView(_effectsShown, RowHeight, MakeEffectRow, BindEffectRow)
        {
            selectionType = SelectionType.Single,
        };
        _effectList.AddToClassList("gfx-list");
        _effectList.selectionChanged += OnEffectSelected;
        _effectList.itemsChosen += OnEffectChosen;
        card.Add(_effectList);

        _nanoList = new ListView(_nanosShown, RowHeight, MakeNanoRow, BindNanoRow)
        {
            selectionType = SelectionType.Single,
        };
        _nanoList.AddToClassList("gfx-list");
        _nanoList.selectionChanged += OnNanoSelected;
        _nanoList.itemsChosen += OnNanoChosen;
        card.Add(_nanoList);

        _hint = Text(string.Empty, "gfx-hint");
        card.Add(_hint);
        card.Add(Text(
            "Verified = rebuilt from stock and checked · Unverified = earlier port, not rechecked · Approx = stand-in · Missing = not drawn",
            "gfx-hint"));

        SetMode(false);
        return card;
    }

    VisualElement BuildStatusBar()
    {
        VisualElement bar = El("gfx-statusbar");
        _status = Text(string.Empty, "gfx-status");
        bar.Add(_status);
        return bar;
    }

    // ---- Effect list ----

    static VisualElement MakeEffectRow()
    {
        VisualElement row = El("gfx-item");
        row.Add(Text(string.Empty, "gfx-item-id"));
        row.Add(Text(string.Empty, "gfx-chip"));
        row.Add(Text(string.Empty, "gfx-item-class"));
        row.Add(Text(string.Empty, "gfx-badge"));
        return row;
    }

    void BindEffectRow(VisualElement element, int index)
    {
        if (index < 0 || index >= _effectsShown.Count)
            return;

        EffectRow row = _effectsShown[index];
        ((Label)element[0]).text = row.Id.ToString();

        var chip = (Label)element[1];
        chip.text = CategoryLabel(row.Category);
        SetVariant(chip, "gfx-chip", CategoryClass(row.Category));

        ((Label)element[2]).text = $"0x{row.TypeCode:X}  {row.StockClass ?? "unidentified"}";

        var badge = (Label)element[3];
        badge.text = EffectCoverage.Label(row.Status);
        SetVariant(badge, "gfx-badge", StatusClass(row.Status));
    }

    void RebuildEffects(GfxTest_DEV test)
    {
        GfxTweakCatalog catalog = test.Catalog;
        _coverage = catalog != null
            ? new EffectCoverage(id => catalog.TryGet(id, out GfxTweakRecord r) ? r : null)
            : null;

        _effects.Clear();
        IReadOnlyList<int> ids = test.CatalogIds;
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            GfxTweakRecord record = null;
            catalog?.TryGet(id, out record);
            int typeCode = record != null ? record.TypeCode : 0;
            EffectCategory category = EffectCategory.Unidentified;
            string stockClass = null;
            if (EffectTypeCatalog.TryGet(typeCode, out EffectTypeInfo info))
            {
                category = info.Category;
                stockClass = info.StockClass;
            }

            EffectCoverage.Result coverage = _coverage != null
                ? _coverage.Of(id)
                : new EffectCoverage.Result(EffectStatus.Missing, null);
            _effects.Add(new EffectRow
            {
                Id = id,
                TypeCode = typeCode,
                Category = category,
                StockClass = stockClass,
                Status = coverage.Status,
                Search = $"{id} 0x{typeCode:x} {typeCode} {stockClass} {category} {EffectCoverage.Label(coverage.Status)} {string.Join(" ", coverage.Gaps)}"
                    .ToLowerInvariant(),
            });
        }

        _nanoVersion = -1;
        _nanos.Clear();
        if (_nanoMode)
            RebuildNanos(test);
        ApplyFilter();
    }

    // ---- Nano list ----

    static VisualElement MakeNanoRow()
    {
        VisualElement row = El("gfx-item");
        row.Add(Text(string.Empty, "gfx-item-id"));
        row.Add(Text(string.Empty, "gfx-item-class", "gfx-item-name"));
        for (int i = 0; i < RoleLetters.Length; i++)
            row.Add(Text(RoleLetters[i], "gfx-dot"));
        row.Add(Text(string.Empty, "gfx-badge"));
        return row;
    }

    void BindNanoRow(VisualElement element, int index)
    {
        if (index < 0 || index >= _nanosShown.Count)
            return;

        NanoRow row = _nanosShown[index];
        ((Label)element[0]).text = row.Id.ToString();
        ((Label)element[1]).text = row.Name;
        for (int i = 0; i < RoleNames.Length; i++)
        {
            var dot = (Label)element[2 + i];
            EffectStatus? s = row.Statuses[i];
            SetVariant(dot, "gfx-dot", s.HasValue ? StatusClass(s.Value) : "is-none");
            dot.tooltip = s.HasValue ? $"{RoleNames[i]} {row.Effects[i]}: {EffectCoverage.Label(s.Value)}" : $"no {RoleNames[i]}";
        }

        var badge = (Label)element[2 + RoleNames.Length];
        badge.text = EffectCoverage.Label(row.Status);
        SetVariant(badge, "gfx-badge", StatusClass(row.Status));
    }

    /// <summary>Reads every nano record's four effect stats (about a second for the whole RDB).</summary>
    void RebuildNanos(GfxTest_DEV test)
    {
        _nanos.Clear();
        _nanoVersion = _catalogVersion;
        ResourceDatabase database = test.Database;
        if (database?.Rdb == null || _coverage == null)
            return;

        int type = NanoRecordType();
        if (type == 0 || !database.Rdb.RecordTypeToId.TryGetValue(type, out Dictionary<int, ulong> records))
            return;

        foreach (int id in records.Keys.OrderBy(k => k))
        {
            NanoObject nano;
            try
            {
                nano = database.Get<NanoObject>(id);
            }
            catch (Exception)
            {
                continue;
            }

            if (!TryReadEffects(nano, out int[] effects))
                continue;

            var statuses = new EffectStatus?[RoleNames.Length];
            EffectStatus worst = EffectStatus.Verified;
            var search = new System.Text.StringBuilder();
            search.Append(id).Append(' ').Append(nano.Name).Append(' ');
            for (int i = 0; i < RoleNames.Length; i++)
            {
                if (effects[i] == 0)
                    continue;
                EffectCoverage.Result r = _coverage.Of(effects[i]);
                statuses[i] = r.Status;
                if (r.Status < worst)
                    worst = r.Status;
                search.Append(effects[i]).Append(' ').Append(string.Join(" ", r.Gaps)).Append(' ');
            }
            search.Append(EffectCoverage.Label(worst));

            _nanos.Add(new NanoRow
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(nano.Name) ? "(unnamed)" : nano.Name.Trim(),
                Effects = effects,
                Statuses = statuses,
                Status = worst,
                Search = search.ToString().ToLowerInvariant(),
            });
        }
    }

    static bool TryReadEffects(NanoObject nano, out int[] effects)
    {
        effects = new int[RoleNames.Length];
        if (nano?.Stats == null)
            return false;

        bool any = false;
        foreach (var kv in nano.Stats)
        {
            int stat = Convert.ToInt32(kv.Key);
            int slot = stat == StatCast ? 0
                : stat == StatTracer ? 1
                : stat == StatImpact ? 2
                : stat == StatHit ? 3
                : stat == StatBuff ? 4
                : -1;
            if (slot < 0)
                continue;
            int value = unchecked((int)Convert.ToUInt32(kv.Value));
            if (value <= 0 || value == EffectTypeTags.RejectedEffectId)
                continue;
            effects[slot] = value;
            any = true;
        }
        return any;
    }

    static int _nanoRecordType;

    /// <summary>The RDB record type NanoObject is stored under (its RDBRecord attribute).</summary>
    static int NanoRecordType()
    {
        if (_nanoRecordType != 0)
            return _nanoRecordType;

        foreach (object attr in typeof(NanoObject).GetCustomAttributes(true))
        {
            PropertyInfo prop = attr.GetType().GetProperty("RecordTypeID");
            if (prop != null)
            {
                _nanoRecordType = Convert.ToInt32(prop.GetValue(attr));
                break;
            }
        }
        return _nanoRecordType;
    }

    // ---- Filtering ----

    void SetMode(bool nanos)
    {
        _nanoMode = nanos;
        _effectsTab.EnableInClassList("is-active", !nanos);
        _nanosTab.EnableInClassList("is-active", nanos);
        _categoryRow.style.display = nanos ? DisplayStyle.None : DisplayStyle.Flex;
        _effectList.style.display = nanos ? DisplayStyle.None : DisplayStyle.Flex;
        _nanoList.style.display = nanos ? DisplayStyle.Flex : DisplayStyle.None;
        _hint.text = nanos
            ? "Click selects the nano · double-click or Enter casts it · dots: cast, tracer, impact, hit, buff"
            : "Click selects · double-click or Enter spawns at look · Game view: WASD/QE fly, RMB look, Space spawn, X clear";

        if (nanos && _bound != null && _nanoVersion != _catalogVersion)
            RebuildNanos(_bound);
        ApplyFilter();
    }

    void ApplyFilter()
    {
        if (_effectList == null)
            return;

        string[] terms = _search.Trim().ToLowerInvariant()
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        EffectStatus? status = Statuses[_statusFilter].Status;

        if (_nanoMode)
        {
            _nanosShown.Clear();
            var counts = new int[Statuses.Length];
            foreach (NanoRow row in _nanos)
            {
                if (!Matches(row.Search, terms))
                    continue;
                CountStatus(counts, row.Status);
                if (status == null || row.Status == status)
                    _nanosShown.Add(row);
            }

            SetChipCounts(_statusChips, Statuses.Select(s => s.Label).ToArray(), counts, _statusFilter);
            _countLabel.text = $"{_nanosShown.Count:N0} / {_nanos.Count:N0} nanos";
            _nanoList.RefreshItems();
            if (_bound != null)
                SelectNano(_bound.NanoId);
            return;
        }

        _effectsShown.Clear();
        var categoryCounts = new int[Categories.Length];
        var statusCounts = new int[Statuses.Length];
        foreach (EffectRow row in _effects)
        {
            if (!Matches(row.Search, terms))
                continue;

            bool inStatus = status == null || row.Status == status;
            bool inCategory = InCategory(row.Category, _categoryFilter);
            if (inStatus)
            {
                for (int c = 0; c < Categories.Length; c++)
                {
                    if (InCategory(row.Category, c))
                        categoryCounts[c]++;
                }
            }
            if (inCategory)
                CountStatus(statusCounts, row.Status);
            if (inStatus && inCategory)
                _effectsShown.Add(row);
        }

        SetChipCounts(_categoryChips, Categories.Select(c => c.Label).ToArray(), categoryCounts, _categoryFilter);
        SetChipCounts(_statusChips, Statuses.Select(s => s.Label).ToArray(), statusCounts, _statusFilter);
        _countLabel.text = $"{_effectsShown.Count:N0} / {_effects.Count:N0} effects";
        _effectList.RefreshItems();
        if (_bound != null)
            SelectEffect(_bound.EffectId);
    }

    static bool Matches(string haystack, string[] terms)
    {
        for (int t = 0; t < terms.Length; t++)
        {
            if (!haystack.Contains(terms[t]))
                return false;
        }
        return true;
    }

    static void CountStatus(int[] counts, EffectStatus status)
    {
        counts[0]++;
        for (int i = 1; i < Statuses.Length; i++)
        {
            if (Statuses[i].Status == status)
                counts[i]++;
        }
    }

    static void SetChipCounts(List<Button> chips, string[] labels, int[] counts, int active)
    {
        for (int i = 0; i < chips.Count; i++)
        {
            chips[i].text = $"{labels[i]}  {counts[i]:N0}";
            chips[i].EnableInClassList("is-active", i == active);
        }
    }

    static bool InCategory(EffectCategory category, int filter)
    {
        EffectCategory[] set = Categories[filter].Categories;
        return set.Length == 0 || Array.IndexOf(set, category) >= 0;
    }

    void SelectEffect(int effectId) => Select(_effectList, _effectsShown.FindIndex(r => r.Id == effectId));

    void SelectNano(int nanoId) => Select(_nanoList, _nanosShown.FindIndex(r => r.Id == nanoId));

    static void Select(ListView list, int index)
    {
        if (list == null)
            return;
        if (index < 0)
        {
            list.ClearSelection();
            return;
        }
        if (list.selectedIndex != index)
        {
            list.SetSelectionWithoutNotify(new[] { index });
            list.ScrollToItem(index);
        }
    }

    void OnEffectSelected(IEnumerable<object> items)
    {
        if (items.FirstOrDefault() is EffectRow row)
        {
            With(t => t.EffectId = row.Id);
            _effectField.SetValueWithoutNotify(row.Id);
            UpdateRecordInfo();
        }
    }

    void OnEffectChosen(IEnumerable<object> items)
    {
        if (items.FirstOrDefault() is EffectRow row)
        {
            With(t =>
            {
                t.EffectId = row.Id;
                t.SpawnAtLook();
            });
        }
    }

    void OnNanoSelected(IEnumerable<object> items)
    {
        if (items.FirstOrDefault() is NanoRow row)
        {
            With(t => t.NanoId = row.Id);
            _nanoField.SetValueWithoutNotify(row.Id);
            UpdateNanoInfo();
        }
    }

    void OnNanoChosen(IEnumerable<object> items)
    {
        if (items.FirstOrDefault() is NanoRow row)
        {
            With(t =>
            {
                t.NanoId = row.Id;
                t.SpawnFromNano();
            });
        }
    }

    // ---- Refresh ----

    void Refresh()
    {
        GfxTest_DEV test = EditorApplication.isPlaying ? FindFirstObjectByType<GfxTest_DEV>() : null;
        if (test != _bound)
        {
            _bound = test;
            _catalogVersion = -1;
            _nanoInfoId = -1;
            PullFields(force: true);
        }

        bool live = _bound != null;
        _content.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
        _empty.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;
        UpdateFps(live);

        if (!live)
        {
            UpdateEmptyState();
            _livePill.text = "idle";
            _dbPill.text = "—";
            SetVariant(_dbPill, "gfx-pill", null);
            _status.text = EditorApplication.isPlaying ? "No GfxTest_DEV in the open scene." : "Not playing.";
            return;
        }

        if (_bound.CatalogVersion != _catalogVersion)
        {
            _catalogVersion = _bound.CatalogVersion;
            RebuildEffects(_bound);
            _nanoInfoId = -1;
        }

        PullFields(force: false);

        int liveCount = _bound.LiveCount;
        _livePill.text = liveCount == 1 ? "1 effect" : $"{liveCount} effects";
        _clearButton.text = liveCount > 0 ? $"Clear ({liveCount})" : "Clear";
        _dbPill.text = _bound.IsDatabaseOpen ? $"DB · {_bound.CatalogIds.Count:N0}" : "DB closed";
        SetVariant(_dbPill, "gfx-pill", _bound.IsDatabaseOpen ? "is-good" : "is-bad");
        _status.text = _bound.Status ?? string.Empty;
        UpdateRecordInfo();
        if (_nanoInfoId != _bound.NanoId)
            UpdateNanoInfo();
    }

    void UpdateFps(bool live)
    {
        double now = EditorApplication.timeSinceStartup;
        if (!live)
        {
            _fpsTime = 0;
            _fpsPill.text = "— fps";
            SetVariant(_fpsPill, "gfx-pill", null);
            return;
        }

        if (_fpsTime <= 0)
        {
            _fpsTime = now;
            _fpsFrame = Time.frameCount;
            return;
        }

        double elapsed = now - _fpsTime;
        if (elapsed < 0.5)
            return;

        float fps = (float)((Time.frameCount - _fpsFrame) / elapsed);
        _fpsTime = now;
        _fpsFrame = Time.frameCount;
        _fpsPill.text = $"{fps:0} fps";
        SetVariant(_fpsPill, "gfx-pill", fps >= 50f ? "is-good" : fps >= 25f ? "is-warn" : "is-bad");
    }

    void UpdateEmptyState()
    {
        if (EditorApplication.isPlaying)
        {
            _emptyTitle.text = "No GFX Test in this scene";
            _emptyBody.text = "The bench lives in the GFX Test scene. Stop Play mode to open it.";
            _emptyAction.text = "Stop Play mode";
            return;
        }

        bool inScene = FindFirstObjectByType<GfxTest_DEV>() != null;
        _emptyTitle.text = "Not in Play mode";
        _emptyBody.text = inScene
            ? "The GFX Test scene is open. Enter Play mode to load the catalog and the two casters."
            : "Open the GFX Test scene, then enter Play mode.";
        _emptyAction.text = inScene ? "Enter Play mode" : "Open GFX Test scene";
    }

    void OnEmptyAction()
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        if (FindFirstObjectByType<GfxTest_DEV>() != null)
        {
            EditorApplication.isPlaying = true;
            return;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            GfxTestSceneMenu.OpenScene();
    }

    /// <summary>Pulls values from the scene into fields the user is not editing.</summary>
    void PullFields(bool force)
    {
        if (_bound == null || _aoPath == null)
            return;

        if (force || !IsEditing(_aoPath))
            _aoPath.SetValueWithoutNotify(_bound.AoPath ?? string.Empty);
        if (force || !IsEditing(_effectField))
            _effectField.SetValueWithoutNotify(_bound.EffectId);
        if (force || !IsEditing(_nanoField))
            _nanoField.SetValueWithoutNotify(_bound.NanoId);
        if (force || !IsEditing(_tintField))
            _tintField.SetValueWithoutNotify(_bound.Tint);
    }

    void UpdateRecordInfo()
    {
        if (_bound == null || _recordText == null)
            return;

        int id = _bound.EffectId;
        GfxTweakCatalog catalog = _bound.Catalog;
        if (catalog == null || !catalog.TryGet(id, out GfxTweakRecord record))
        {
            _recordChip.text = "?";
            SetVariant(_recordChip, "gfx-chip", "cat-other");
            _recordBadge.style.display = DisplayStyle.None;
            _recordText.text = catalog == null
                ? "Open the database to inspect effects."
                : $"{id} is not in the catalog; spawning uses the fallback flare.";
            return;
        }

        EffectCategory category = EffectCategory.Unidentified;
        string stockClass = null;
        if (EffectTypeCatalog.TryGet(record.TypeCode, out EffectTypeInfo info))
        {
            category = info.Category;
            stockClass = info.StockClass;
        }

        EffectCoverage.Result coverage = _coverage != null ? _coverage.Of(id) : default;
        _recordChip.text = CategoryLabel(category);
        SetVariant(_recordChip, "gfx-chip", CategoryClass(category));
        _recordBadge.style.display = DisplayStyle.Flex;
        _recordBadge.text = EffectCoverage.Label(coverage.Status);
        SetVariant(_recordBadge, "gfx-badge", StatusClass(coverage.Status));
        string gaps = coverage.Gaps != null && coverage.Gaps.Length > 0 ? " · needs " + string.Join(", ", coverage.Gaps) : string.Empty;
        _recordText.text = $"0x{record.TypeCode:X} {stockClass ?? "unidentified"} · {record.FieldCount} fields{gaps}";
        _recordText.tooltip = _recordText.text;
    }

    void UpdateNanoInfo()
    {
        if (_bound == null || _nanoInfo == null)
            return;

        _nanoInfoId = _bound.NanoId;
        NanoObject nano = null;
        int[] effects = null;
        if (_nanoInfoId > 0 && _bound.Database?.Rdb != null)
        {
            try
            {
                nano = _bound.Database.Get<NanoObject>(_nanoInfoId);
            }
            catch (Exception)
            {
                nano = null;
            }
        }

        if (nano == null || _coverage == null || !TryReadEffects(nano, out effects))
        {
            _nanoInfo.style.display = DisplayStyle.None;
            return;
        }

        _nanoInfo.style.display = DisplayStyle.Flex;
        _nanoName.text = string.IsNullOrWhiteSpace(nano.Name) ? $"{_nanoInfoId}" : nano.Name.Trim();
        for (int i = 0; i < RoleNames.Length; i++)
        {
            Label role = _nanoRoles[i];
            if (effects[i] == 0)
            {
                role.text = RoleNames[i];
                SetVariant(role, "gfx-role", "is-none");
                role.tooltip = $"no {RoleNames[i]} effect";
                continue;
            }

            EffectCoverage.Result r = _coverage.Of(effects[i]);
            role.text = $"{RoleNames[i]} {effects[i]}";
            SetVariant(role, "gfx-role", StatusClass(r.Status));
            role.tooltip = r.Gaps.Length > 0
                ? $"{EffectCoverage.Label(r.Status)}: needs {string.Join(", ", r.Gaps)}"
                : EffectCoverage.Label(r.Status);
        }
    }

    // ---- Actions ----

    void With(Action<GfxTest_DEV> action)
    {
        if (_bound != null)
            action(_bound);
    }

    void BrowseAoPath()
    {
        string start = _aoPath != null ? _aoPath.value : string.Empty;
        string picked = EditorUtility.OpenFolderPanel("Anarchy Online folder", start, string.Empty);
        if (string.IsNullOrEmpty(picked))
            return;

        _aoPath.value = picked;
    }

    // ---- Helpers ----

    StyleSheet LoadStyleSheet()
    {
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        string dir = string.IsNullOrEmpty(scriptPath) ? "Assets/Editor/GfxTest" : System.IO.Path.GetDirectoryName(scriptPath);
        return AssetDatabase.LoadAssetAtPath<StyleSheet>($"{dir}/GfxTestWindow.uss".Replace('\\', '/'));
    }

    static bool IsEditing(VisualElement field)
    {
        var focused = field.focusController?.focusedElement as VisualElement;
        return focused != null && (focused == field || field.Contains(focused));
    }

    static VisualElement Card(string title)
    {
        VisualElement card = El("gfx-card");
        card.Add(Text(title.ToUpperInvariant(), "gfx-card-title"));
        return card;
    }

    static VisualElement El(params string[] classes)
    {
        var element = new VisualElement();
        foreach (string c in classes)
            element.AddToClassList(c);
        return element;
    }

    static Label Text(string text, params string[] classes)
    {
        var label = new Label(text);
        foreach (string c in classes)
            label.AddToClassList(c);
        return label;
    }

    static Button Btn(string text, Action onClick, params string[] classes)
    {
        var button = new Button(onClick) { text = text };
        button.AddToClassList("gfx-button");
        foreach (string c in classes)
            button.AddToClassList(c);
        return button;
    }

    static void SetVariant(VisualElement element, string baseClass, string variant)
    {
        element.ClearClassList();
        element.AddToClassList(baseClass);
        if (!string.IsNullOrEmpty(variant))
            element.AddToClassList(variant);
    }

    static string StatusClass(EffectStatus status) => status switch
    {
        EffectStatus.Verified => "st-verified",
        EffectStatus.Unverified => "st-unverified",
        EffectStatus.Approximated => "st-approx",
        _ => "st-missing",
    };

    static string CategoryLabel(EffectCategory category) => category switch
    {
        EffectCategory.Sprite => "sprite",
        EffectCategory.Composite => "composite",
        EffectCategory.Particle => "particle",
        EffectCategory.PointCloud => "points",
        EffectCategory.Tracer => "tracer",
        EffectCategory.Mesh => "mesh",
        EffectCategory.Screen => "screen",
        EffectCategory.Audio => "audio",
        EffectCategory.Buff => "buff",
        EffectCategory.None => "none",
        _ => "unknown",
    };

    static string CategoryClass(EffectCategory category) => category switch
    {
        EffectCategory.Sprite => "cat-sprite",
        EffectCategory.Composite => "cat-composite",
        EffectCategory.Particle => "cat-particle",
        EffectCategory.PointCloud => "cat-points",
        EffectCategory.Tracer => "cat-tracer",
        EffectCategory.Mesh => "cat-mesh",
        _ => "cat-other",
    };
}
