using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SoundSense.Editor
{
    /// <summary>
    /// SoundSense Editor Window — offline audio term reference for Unity.
    /// Window > SoundSense to open.
    /// </summary>
    public class SoundSenseWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────
        string _searchQuery = "";
        string _prevSearchQuery = "";
        List<AudioTermData> _searchResults;
        AudioTermData _selectedTerm;
        int _tierIndex; // 0=beginner, 1=intermediate, 2=advanced
        string _selectedCategory;
        Vector2 _listScroll;
        Vector2 _detailScroll;
        int _tab; // 0=Search, 1=Categories, 2=Context, 3=Import Advisor

        // Context detection
        string _lastContextSummary;
        List<string> _contextTermIds;

        // Import advisor
        List<SoundSenseImportAdvisor.ImportAdvice> _importAdvice;

        // ── Styles (lazy init) ────────────────────────────────────
        static GUIStyle _headerStyle;
        static GUIStyle _subHeaderStyle;
        static GUIStyle _bodyStyle;
        static GUIStyle _tipStyle;
        static GUIStyle _gotchaStyle;
        static GUIStyle _tagStyle;
        static GUIStyle _categoryBadgeStyle;
        static bool _stylesReady;

        static readonly string[] TierNames = { "Beginner", "Intermediate", "Advanced" };
        static readonly string[] TabNames = { "Search", "Categories", "Context", "Import Advisor" };

        [MenuItem("Window/SoundSense")]
        public static void ShowWindow()
        {
            var window = GetWindow<SoundSenseWindow>();
            window.titleContent = new GUIContent("SoundSense", EditorGUIUtility.IconContent("d_AudioSource Icon").image);
            window.minSize = new Vector2(360, 400);
        }

        void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            _searchResults = new List<AudioTermData>(SoundSenseDatabase.AllTerms);
            RefreshContext();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged()
        {
            RefreshContext();
            RefreshImportAdvisor();
            Repaint();
        }

        void InitStyles()
        {
            if (_stylesReady) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                wordWrap = true,
                margin = new RectOffset(0, 0, 8, 4)
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                wordWrap = true,
                margin = new RectOffset(0, 0, 8, 2)
            };

            _bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                richText = true,
                margin = new RectOffset(0, 0, 2, 6)
            };

            _tipStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _gotchaStyle = new GUIStyle(_tipStyle);

            _tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.5f, 0.7f, 1f) }
            };

            _categoryBadgeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                fixedHeight = 20
            };

            _stylesReady = true;
        }

        // ── Main GUI ──────────────────────────────────────────────

        void OnGUI()
        {
            InitStyles();

            // Tab bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool active = _tab == i;
                if (GUILayout.Toggle(active, TabNames[i], EditorStyles.toolbarButton, GUILayout.MinWidth(60)) && !active)
                {
                    _tab = i;
                    if (i == 2) RefreshContext();
                    if (i == 3) RefreshImportAdvisor();
                }
            }
            EditorGUILayout.EndHorizontal();

            switch (_tab)
            {
                case 0: DrawSearchTab(); break;
                case 1: DrawCategoriesTab(); break;
                case 2: DrawContextTab(); break;
                case 3: DrawImportAdvisorTab(); break;
            }
        }

        // ── Search Tab ────────────────────────────────────────────

        void DrawSearchTab()
        {
            // Search bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.SetNextControlName("SearchField");
            _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
            {
                _searchQuery = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // Update results when query changes
            if (_searchQuery != _prevSearchQuery)
            {
                _searchResults = SoundSenseDatabase.Search(_searchQuery);
                _prevSearchQuery = _searchQuery;
            }

            // Tier selector
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Level:", GUILayout.Width(38));
            _tierIndex = GUILayout.Toolbar(_tierIndex, TierNames, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // Split: list + detail
            EditorGUILayout.BeginHorizontal();

            // Left panel — term list
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Min(200, position.width * 0.35f)));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            if (_searchResults != null)
            {
                GUILayout.Label($"{_searchResults.Count} terms", EditorStyles.centeredGreyMiniLabel);
                foreach (var term in _searchResults)
                {
                    bool selected = _selectedTerm != null && _selectedTerm.id == term.id;
                    var style = selected ? "selectionRect" : "label";
                    if (GUILayout.Button(term.name, style))
                    {
                        _selectedTerm = term;
                        _detailScroll = Vector2.zero;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Right panel — term detail
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (_selectedTerm != null)
                DrawTermDetail(_selectedTerm);
            else
                GUILayout.Label("Select a term to view details.", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ── Categories Tab ────────────────────────────────────────

        void DrawCategoriesTab()
        {
            // Tier selector
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Level:", GUILayout.Width(38));
            _tierIndex = GUILayout.Toolbar(_tierIndex, TierNames, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            // Left: category list
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Min(160, position.width * 0.3f)));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            var categories = SoundSenseDatabase.Categories;
            foreach (var cat in categories)
            {
                var count = SoundSenseDatabase.GetByCategory(cat).Count;
                bool selected = _selectedCategory == cat;
                var label = $"{FormatCategory(cat)} ({count})";
                if (GUILayout.Toggle(selected, label, _categoryBadgeStyle) && !selected)
                {
                    _selectedCategory = cat;
                    _selectedTerm = null;
                    _detailScroll = Vector2.zero;
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Right: terms in category + detail
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (!string.IsNullOrEmpty(_selectedCategory))
            {
                if (_selectedTerm != null && _selectedTerm.category == _selectedCategory)
                {
                    if (GUILayout.Button("<  Back to " + FormatCategory(_selectedCategory), EditorStyles.miniButton, GUILayout.Width(200)))
                        _selectedTerm = null;
                    EditorGUILayout.Space(4);
                    DrawTermDetail(_selectedTerm);
                }
                else
                {
                    var terms = SoundSenseDatabase.GetByCategory(_selectedCategory);
                    GUILayout.Label(FormatCategory(_selectedCategory), _headerStyle);
                    GUILayout.Label($"{terms.Count} terms", EditorStyles.centeredGreyMiniLabel);
                    EditorGUILayout.Space(4);

                    foreach (var term in terms)
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                        EditorGUILayout.BeginVertical();
                        GUILayout.Label(term.name, EditorStyles.boldLabel);
                        GUILayout.Label(GetOneLiner(term), _bodyStyle);
                        EditorGUILayout.EndVertical();
                        if (GUILayout.Button("View", GUILayout.Width(50), GUILayout.Height(30)))
                        {
                            _selectedTerm = term;
                            _detailScroll = Vector2.zero;
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space(2);
                    }
                }
            }
            else
            {
                GUILayout.Label("Select a category.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ── Context Tab ───────────────────────────────────────────

        void DrawContextTab()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Level:", GUILayout.Width(38));
            _tierIndex = GUILayout.Toolbar(_tierIndex, TierNames, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (string.IsNullOrEmpty(_lastContextSummary))
            {
                EditorGUILayout.HelpBox(
                    "Select a GameObject with audio components (AudioSource, AudioListener, filters, etc.) to see relevant terms.",
                    MessageType.Info);
                return;
            }

            GUILayout.Label("Detected: " + _lastContextSummary, _subHeaderStyle);
            EditorGUILayout.Space(4);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (_selectedTerm != null)
            {
                if (GUILayout.Button("<  Back to context results", EditorStyles.miniButton, GUILayout.Width(200)))
                    _selectedTerm = null;
                EditorGUILayout.Space(4);
                DrawTermDetail(_selectedTerm);
            }
            else if (_contextTermIds != null && _contextTermIds.Count > 0)
            {
                GUILayout.Label($"{_contextTermIds.Count} relevant terms found:", _bodyStyle);
                EditorGUILayout.Space(4);

                foreach (var id in _contextTermIds)
                {
                    var term = SoundSenseDatabase.GetById(id);
                    if (term == null) continue;

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.BeginVertical();
                    GUILayout.Label(term.name, EditorStyles.boldLabel);
                    GUILayout.Label(GetOneLiner(term), _bodyStyle);
                    EditorGUILayout.EndVertical();
                    if (GUILayout.Button("View", GUILayout.Width(50), GUILayout.Height(30)))
                    {
                        _selectedTerm = term;
                        _detailScroll = Vector2.zero;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Import Advisor Tab ────────────────────────────────────

        void DrawImportAdvisorTab()
        {
            EditorGUILayout.HelpBox(
                "Select one or more AudioClip assets in the Project window to get import setting recommendations.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            if (_importAdvice == null || _importAdvice.Count == 0)
            {
                GUILayout.Label("No AudioClip assets selected.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            foreach (var advice in _importAdvice)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.Label(advice.clipName, _subHeaderStyle);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Duration: {advice.durationSeconds:F2}s", GUILayout.Width(120));
                GUILayout.Label($"Channels: {advice.channels}", GUILayout.Width(90));
                GUILayout.Label($"Rate: {advice.frequency}Hz");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Load Type: {advice.currentLoadType}", GUILayout.Width(200));
                GUILayout.Label($"Format: {advice.currentCompression}");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
                GUILayout.Label("Recommendation", EditorStyles.boldLabel);
                GUILayout.Label(advice.recommendation, _tipStyle);

                EditorGUILayout.Space(2);
                GUILayout.Label(advice.reason, _bodyStyle);

                // Related term links
                if (advice.relatedTermIds != null && advice.relatedTermIds.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Learn more:", EditorStyles.miniLabel, GUILayout.Width(70));
                    foreach (var id in advice.relatedTermIds)
                    {
                        var term = SoundSenseDatabase.GetById(id);
                        if (term == null) continue;
                        if (GUILayout.Button(term.name, EditorStyles.linkLabel))
                        {
                            _selectedTerm = term;
                            _tab = 0; // Switch to search tab to show detail
                            _detailScroll = Vector2.zero;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Term Detail View ──────────────────────────────────────

        void DrawTermDetail(AudioTermData term)
        {
            // Name + category
            GUILayout.Label(term.name, _headerStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(FormatCategory(term.category), _categoryBadgeStyle, GUILayout.Width(100));
            // Tags
            if (term.tags != null)
            {
                foreach (var tag in term.tags)
                    GUILayout.Label(tag, _tagStyle);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Tier content
            switch (_tierIndex)
            {
                case 0: DrawBeginnerTier(term); break;
                case 1: DrawIntermediateTier(term); break;
                case 2: DrawAdvancedTier(term); break;
            }

            // Unity adapter
            if (term.unityAdapter != null)
            {
                EditorGUILayout.Space(8);
                DrawSeparator("Unity Context");

                if (!string.IsNullOrEmpty(term.unityAdapter.location))
                {
                    GUILayout.Label("<b>Where to find it:</b>", _bodyStyle);
                    GUILayout.Label(term.unityAdapter.location, _bodyStyle);
                }

                if (!string.IsNullOrEmpty(term.unityAdapter.apiRef))
                {
                    EditorGUILayout.Space(2);
                    GUILayout.Label("<b>API Reference:</b>", _bodyStyle);
                    if (term.unityAdapter.apiRef.StartsWith("http"))
                    {
                        if (GUILayout.Button(term.unityAdapter.apiRef, EditorStyles.linkLabel))
                            Application.OpenURL(term.unityAdapter.apiRef);
                    }
                    else
                    {
                        GUILayout.Label(term.unityAdapter.apiRef, _bodyStyle);
                    }
                }

                if (!string.IsNullOrEmpty(term.unityAdapter.importTip))
                {
                    EditorGUILayout.Space(4);
                    GUILayout.Label("Tip", EditorStyles.boldLabel);
                    GUILayout.Label(term.unityAdapter.importTip, _tipStyle);
                }

                if (!string.IsNullOrEmpty(term.unityAdapter.gotcha))
                {
                    EditorGUILayout.Space(4);
                    GUILayout.Label("Watch Out", EditorStyles.boldLabel);
                    GUILayout.Label(term.unityAdapter.gotcha, _gotchaStyle);
                }
            }

            // Misconceptions
            if (term.misconceptions != null && term.misconceptions.Length > 0)
            {
                EditorGUILayout.Space(8);
                DrawSeparator("Common Misconceptions");
                foreach (var m in term.misconceptions)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("!", EditorStyles.boldLabel, GUILayout.Width(14));
                    GUILayout.Label(m, _bodyStyle);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
            }

            // Related terms
            if (term.related != null && term.related.Length > 0)
            {
                EditorGUILayout.Space(8);
                DrawSeparator("Related Terms");
                EditorGUILayout.BeginHorizontal();
                int count = 0;
                foreach (var relId in term.related)
                {
                    var rel = SoundSenseDatabase.GetById(relId);
                    if (rel == null) continue;
                    if (GUILayout.Button(rel.name, EditorStyles.miniButton, GUILayout.MaxWidth(140)))
                    {
                        _selectedTerm = rel;
                        _detailScroll = Vector2.zero;
                    }
                    count++;
                    // Wrap every 3
                    if (count % 3 == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(16);
        }

        void DrawBeginnerTier(AudioTermData term)
        {
            var t = term.tiers?.beginner;
            if (t == null) return;

            GUILayout.Label(t.oneLiner, _bodyStyle);

            if (!string.IsNullOrEmpty(t.analogy))
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("Analogy", EditorStyles.boldLabel);
                GUILayout.Label(t.analogy, _tipStyle);
            }

            if (!string.IsNullOrEmpty(t.whatToDo))
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("What to Do", EditorStyles.boldLabel);
                GUILayout.Label(t.whatToDo, _bodyStyle);
            }
        }

        void DrawIntermediateTier(AudioTermData term)
        {
            var t = term.tiers?.intermediate;
            if (t == null) return;

            GUILayout.Label(t.oneLiner, _bodyStyle);

            if (!string.IsNullOrEmpty(t.details))
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("Details", EditorStyles.boldLabel);
                GUILayout.Label(t.details, _bodyStyle);
            }

            if (!string.IsNullOrEmpty(t.whenToUse))
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("When to Use", EditorStyles.boldLabel);
                GUILayout.Label(t.whenToUse, _tipStyle);
            }
        }

        void DrawAdvancedTier(AudioTermData term)
        {
            var t = term.tiers?.advanced;
            if (t == null) return;

            GUILayout.Label(t.oneLiner, _bodyStyle);

            if (!string.IsNullOrEmpty(t.technicalNotes))
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("Technical Notes", EditorStyles.boldLabel);
                GUILayout.Label(t.technicalNotes, _bodyStyle);
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        void RefreshContext()
        {
            _lastContextSummary = SoundSenseContextDetector.GetContextSummary();
            _contextTermIds = SoundSenseContextDetector.GetContextTermIds();
        }

        void RefreshImportAdvisor()
        {
            _importAdvice = SoundSenseImportAdvisor.AnalyzeSelection();
        }

        string GetOneLiner(AudioTermData term)
        {
            if (term.tiers == null) return "";
            switch (_tierIndex)
            {
                case 0: return term.tiers.beginner?.oneLiner ?? "";
                case 1: return term.tiers.intermediate?.oneLiner ?? "";
                case 2: return term.tiers.advanced?.oneLiner ?? "";
                default: return "";
            }
        }

        static string FormatCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return "";
            return char.ToUpper(category[0]) + category.Substring(1);
        }

        void DrawSeparator(string label)
        {
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
            EditorGUILayout.Space(4);
            GUILayout.Label(label, _subHeaderStyle);
        }
    }
}
