using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SoundSense.Editor
{
    /// <summary>
    /// Analyzes selected AudioClip assets and provides import setting recommendations
    /// based on SoundSense knowledge base.
    /// </summary>
    public static class SoundSenseImportAdvisor
    {
        public struct ImportAdvice
        {
            public string clipName;
            public float durationSeconds;
            public int channels;
            public int frequency;
            public AudioClipLoadType currentLoadType;
            public AudioCompressionFormat currentCompression;
            public string recommendation;
            public string reason;
            public List<string> relatedTermIds;
        }

        /// <summary>
        /// Analyze the currently selected audio assets and return recommendations.
        /// </summary>
        public static List<ImportAdvice> AnalyzeSelection()
        {
            var results = new List<ImportAdvice>();

            foreach (var obj in Selection.objects)
            {
                if (obj is AudioClip clip)
                {
                    var path = AssetDatabase.GetAssetPath(clip);
                    if (string.IsNullOrEmpty(path)) continue;

                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) continue;

                    var settings = importer.defaultSampleSettings;
                    var advice = Analyze(clip, settings);
                    results.Add(advice);
                }
            }

            return results;
        }

        static ImportAdvice Analyze(AudioClip clip, AudioImporterSampleSettings settings)
        {
            var advice = new ImportAdvice
            {
                clipName = clip.name,
                durationSeconds = clip.length,
                channels = clip.channels,
                frequency = clip.frequency,
                currentLoadType = settings.loadType,
                currentCompression = settings.compressionFormat,
                relatedTermIds = new List<string> { "audio-clip" }
            };

            // Short SFX (< 1 second)
            if (clip.length < 1f)
            {
                advice.relatedTermIds.Add("decompress-on-load");
                advice.relatedTermIds.Add("adpcm");

                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    advice.recommendation = "Use Decompress On Load with ADPCM compression.";
                    advice.reason = "Short clips (< 1s) benefit from instant playback with minimal memory cost. ADPCM gives ~3.5:1 compression with fast decoding.";
                }
                else
                {
                    advice.recommendation = "Settings look good for a short SFX clip.";
                    advice.reason = "Decompress On Load is ideal for frequently played short sounds.";
                }
            }
            // Medium clips (1-10 seconds)
            else if (clip.length < 10f)
            {
                advice.relatedTermIds.Add("compressed-in-memory");
                advice.relatedTermIds.Add("vorbis");

                if (settings.loadType == AudioClipLoadType.Streaming)
                {
                    advice.recommendation = "Consider Compressed In Memory instead of Streaming.";
                    advice.reason = "Medium-length clips don't benefit much from streaming. Compressed In Memory (Vorbis) avoids disk I/O overhead while keeping memory reasonable.";
                }
                else
                {
                    advice.recommendation = "Settings look reasonable for a medium-length clip.";
                    advice.reason = "Compressed In Memory with Vorbis is typically best for clips in this range.";
                }
            }
            // Long clips (> 10 seconds) — music, ambience
            else
            {
                advice.relatedTermIds.Add("streaming");
                advice.relatedTermIds.Add("vorbis");

                if (settings.loadType != AudioClipLoadType.Streaming)
                {
                    advice.recommendation = "Use Streaming with Vorbis compression.";
                    advice.reason = $"This clip is {clip.length:F1}s long. Streaming avoids loading the entire clip into memory, which is critical for music and ambient tracks.";
                }
                else
                {
                    advice.recommendation = "Streaming is the right choice for this long clip.";
                    advice.reason = "Long audio files should stream to keep memory usage low.";
                }
            }

            // Stereo warning for 3D sounds
            if (clip.channels > 1)
            {
                advice.relatedTermIds.Add("spatial-blend");
                advice.recommendation += "\n\nNote: This clip is stereo. If it's a 3D positional sound, consider forcing it to Mono in import settings to halve memory and ensure correct spatialization.";
            }

            return advice;
        }
    }
}
