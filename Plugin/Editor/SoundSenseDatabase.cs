using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SoundSense.Editor
{
    // ── JSON data models ──────────────────────────────────────────

    [Serializable]
    public class UnityAdapter
    {
        public string location;
        public string apiRef;
        public string importTip;
        public string gotcha;
    }

    [Serializable]
    public class BeginnerTier
    {
        public string oneLiner;
        public string analogy;
        public string whatToDo;
    }

    [Serializable]
    public class IntermediateTier
    {
        public string oneLiner;
        public string details;
        public string whenToUse;
    }

    [Serializable]
    public class VsEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class AdvancedTier
    {
        public string oneLiner;
        public string technicalNotes;
        // vs is a dictionary in JSON — we deserialize manually
        [NonSerialized] public Dictionary<string, string> vs;
    }

    [Serializable]
    public class TierContent
    {
        public BeginnerTier beginner;
        public IntermediateTier intermediate;
        public AdvancedTier advanced;
    }

    [Serializable]
    public class AudioTermData
    {
        public string id;
        public string name;
        public string category;
        public string[] tags;
        public string[] related;
        public TierContent tiers;
        public UnityAdapter unityAdapter;
        public string[] misconceptions;
    }

    [Serializable]
    internal class AudioTermArray
    {
        public AudioTermData[] items;
    }

    // ── Database ──────────────────────────────────────────────────

    /// <summary>
    /// Loads and indexes all SoundSense terms from the embedded JSON.
    /// Fully offline — no network requests.
    /// </summary>
    public static class SoundSenseDatabase
    {
        static AudioTermData[] _terms;
        static Dictionary<string, AudioTermData> _byId;
        static Dictionary<string, List<AudioTermData>> _byCategory;
        static string[] _categories;
        static bool _loaded;

        public static AudioTermData[] AllTerms
        {
            get { EnsureLoaded(); return _terms; }
        }

        public static string[] Categories
        {
            get { EnsureLoaded(); return _categories; }
        }

        public static AudioTermData GetById(string id)
        {
            EnsureLoaded();
            _byId.TryGetValue(id, out var term);
            return term;
        }

        public static List<AudioTermData> GetByCategory(string category)
        {
            EnsureLoaded();
            _byCategory.TryGetValue(category, out var list);
            return list ?? new List<AudioTermData>();
        }

        /// <summary>
        /// Fuzzy-ish search: matches name, category, and tags.
        /// Returns results sorted by relevance.
        /// </summary>
        public static List<AudioTermData> Search(string query)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(query))
                return new List<AudioTermData>(_terms);

            var q = query.Trim().ToLowerInvariant();
            var scored = new List<(AudioTermData term, int score)>();

            foreach (var term in _terms)
            {
                int score = 0;
                var nameLower = term.name.ToLowerInvariant();

                // Exact name match
                if (nameLower == q)
                    score += 100;
                // Name starts with query
                else if (nameLower.StartsWith(q))
                    score += 80;
                // Name contains query
                else if (nameLower.Contains(q))
                    score += 60;

                // Category match
                if (term.category != null && term.category.ToLowerInvariant().Contains(q))
                    score += 30;

                // Tag match
                if (term.tags != null)
                {
                    foreach (var tag in term.tags)
                    {
                        if (tag.ToLowerInvariant().Contains(q))
                        {
                            score += 20;
                            break;
                        }
                    }
                }

                // One-liner content match (beginner tier)
                if (term.tiers?.beginner?.oneLiner != null &&
                    term.tiers.beginner.oneLiner.ToLowerInvariant().Contains(q))
                    score += 10;

                if (score > 0)
                    scored.Add((term, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            return scored.Select(s => s.term).ToList();
        }

        /// <summary>
        /// Find terms relevant to a specific Unity component type name.
        /// </summary>
        public static List<AudioTermData> FindByComponentType(string typeName)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(typeName))
                return new List<AudioTermData>();

            var lower = typeName.ToLowerInvariant();
            var results = new List<AudioTermData>();

            foreach (var term in _terms)
            {
                // Match against term name
                if (term.name.ToLowerInvariant().Contains(lower) ||
                    lower.Contains(term.name.ToLowerInvariant()))
                {
                    results.Add(term);
                    continue;
                }

                // Match against Unity adapter location/apiRef
                if (term.unityAdapter != null)
                {
                    if ((term.unityAdapter.location != null && term.unityAdapter.location.ToLowerInvariant().Contains(lower)) ||
                        (term.unityAdapter.apiRef != null && term.unityAdapter.apiRef.ToLowerInvariant().Contains(lower)))
                    {
                        results.Add(term);
                    }
                }
            }

            return results;
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;

            var guids = UnityEditor.AssetDatabase.FindAssets("terms t:TextAsset",
                new[] { "Packages/com.soundsense.editor/Editor/Data" });

            if (guids.Length == 0)
            {
                // Fallback: search all packages
                guids = UnityEditor.AssetDatabase.FindAssets("terms t:TextAsset");
            }

            TextAsset textAsset = null;
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("soundsense") || path.Contains("SoundSense"))
                {
                    textAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (textAsset != null) break;
                }
            }

            if (textAsset == null)
            {
                Debug.LogError("[SoundSense] Could not find terms.json in package data.");
                _terms = Array.Empty<AudioTermData>();
                _byId = new Dictionary<string, AudioTermData>();
                _byCategory = new Dictionary<string, List<AudioTermData>>();
                _categories = Array.Empty<string>();
                _loaded = true;
                return;
            }

            // JsonUtility can't deserialize a root-level array, so we wrap it
            var json = "{\"items\":" + textAsset.text + "}";
            var wrapper = JsonUtility.FromJson<AudioTermArray>(json);
            _terms = wrapper.items ?? Array.Empty<AudioTermData>();

            // Build indexes
            _byId = new Dictionary<string, AudioTermData>();
            _byCategory = new Dictionary<string, List<AudioTermData>>();
            var cats = new HashSet<string>();

            foreach (var term in _terms)
            {
                if (!string.IsNullOrEmpty(term.id))
                    _byId[term.id] = term;

                if (!string.IsNullOrEmpty(term.category))
                {
                    cats.Add(term.category);
                    if (!_byCategory.ContainsKey(term.category))
                        _byCategory[term.category] = new List<AudioTermData>();
                    _byCategory[term.category].Add(term);
                }
            }

            _categories = cats.OrderBy(c => c).ToArray();
            _loaded = true;

            Debug.Log($"[SoundSense] Loaded {_terms.Length} audio terms ({_categories.Length} categories).");
        }

        /// <summary>Force reload from disk (useful after reimport).</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
    }
}
