using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SoundSense.Editor
{
    /// <summary>
    /// Detects audio-related components on the currently selected GameObject
    /// and suggests relevant SoundSense terms.
    /// </summary>
    public static class SoundSenseContextDetector
    {
        // Maps Unity component type names to SoundSense term IDs
        static readonly Dictionary<string, string[]> ComponentToTerms = new Dictionary<string, string[]>
        {
            { "AudioSource", new[] { "audio-source", "audio-clip", "audio-rolloff", "spatial-blend", "play-on-awake" } },
            { "AudioListener", new[] { "audio-listener", "spatialization", "doppler-effect" } },
            { "AudioReverbZone", new[] { "reverb", "reverb-zone", "wet-dry-mix" } },
            { "AudioReverbFilter", new[] { "reverb", "convolution-reverb", "impulse-response" } },
            { "AudioLowPassFilter", new[] { "low-pass-filter", "cutoff-frequency", "occlusion" } },
            { "AudioHighPassFilter", new[] { "high-pass-filter", "cutoff-frequency" } },
            { "AudioDistortionFilter", new[] { "distortion", "clipping" } },
            { "AudioEchoFilter", new[] { "delay", "echo", "feedback" } },
            { "AudioChorusFilter", new[] { "chorus", "modulation" } },
        };

        /// <summary>
        /// Returns term IDs relevant to audio components on the active selection.
        /// </summary>
        public static List<string> GetContextTermIds()
        {
            var termIds = new HashSet<string>();
            var go = Selection.activeGameObject;
            if (go == null) return new List<string>();

            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var typeName = comp.GetType().Name;
                if (ComponentToTerms.TryGetValue(typeName, out var ids))
                {
                    foreach (var id in ids)
                        termIds.Add(id);
                }
            }

            return new List<string>(termIds);
        }

        /// <summary>
        /// Returns a readable summary of detected audio components.
        /// </summary>
        public static string GetContextSummary()
        {
            var go = Selection.activeGameObject;
            if (go == null) return null;

            var audioComponents = new List<string>();
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var typeName = comp.GetType().Name;
                if (ComponentToTerms.ContainsKey(typeName))
                    audioComponents.Add(typeName);
            }

            if (audioComponents.Count == 0) return null;

            return $"{go.name}: {string.Join(", ", audioComponents)}";
        }
    }
}
