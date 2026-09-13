using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;

namespace WebMap
{
    // The world as a chart: every pixel the flat colour of its biome, water one
    // blue, no relief. Sampled from the world generator the way the render is,
    // one row per frame, once per world, and kept beside map.png. For pages that
    // want the world as a reference surface rather than a picture.
    internal static class Chart
    {
        // Bump when the picture's rules change, so a cached file is rebuilt.
        private const string FileName = "chart.v2.png";
        private static volatile byte[] png;
        private static volatile bool building;

        private static readonly Color32 Water = new Color32(43, 79, 122, 255);

        // The render's own biome palette, unshaded; the two white biomes are held
        // just off white so they still read as ground.
        private static Color32 Albedo(Heightmap.Biome b)
        {
            switch (b)
            {
                case Heightmap.Biome.Meadows:     return new Color32(146, 167,  92, 255);
                case Heightmap.Biome.BlackForest: return new Color32(107, 116,  63, 255);
                case Heightmap.Biome.Swamp:       return new Color32(163, 114,  88, 255);
                case Heightmap.Biome.Mountain:    return new Color32(236, 238, 240, 255);
                case Heightmap.Biome.Plains:      return new Color32(231, 171, 120, 255);
                case Heightmap.Biome.Mistlands:   return new Color32( 92,  56, 102, 255);
                case Heightmap.Biome.AshLands:    return new Color32(176,  49,  49, 255);
                case Heightmap.Biome.DeepNorth:   return new Color32(230, 236, 242, 255);
                default:                          return Water;           // Ocean
            }
        }

        public static void Load(string worldDataPath)
        {
            png = null;
            string p = Path.Combine(worldDataPath, FileName);
            try { if (File.Exists(p)) png = File.ReadAllBytes(p); } catch { }
            try { string old = Path.Combine(worldDataPath, "chart.png"); if (File.Exists(old)) File.Delete(old); } catch { }
            if (png == null) StaticCoroutine.Start(Build(worldDataPath));
        }

        private static IEnumerator Build(string worldDataPath)
        {
            if (building) yield break;
            building = true;
            int size = WebMapConfig.TEXTURE_SIZE, half = size / 2;
            float ps = WebMapConfig.PIXEL_SIZE, halfPx = ps / 2f;
            // one class per pixel: 0 = water, else the biome's bit index + 1
            var cls = new byte[size * size];
            float water = ZoneSystem.instance.m_waterLevel;
            for (int y = 0; y < size; y++)
            {
                yield return null;                            // one row per frame, like the render
                float wz = (y - half) * ps + halfPx;
                for (int x = 0; x < size; x++)
                {
                    float wx = (x - half) * ps + halfPx;
                    var biome = WorldGenerator.instance.GetBiome(wx, wz);
                    float h = WorldGenerator.instance.GetBiomeHeight(biome, wx, wz, out Color _);
                    cls[y * size + x] = h < water ? (byte)0 : (byte)(Index(biome) + 1);
                }
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Biome edges are noisy at 12 m a pixel -- a swamp came out as speckle --
                    // so each pixel takes the class most of its 3x3 neighbourhood has.
                    var smooth = new byte[cls.Length];
                    var votes = new int[Classes];
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            Array.Clear(votes, 0, votes.Length);
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int yy = y + dy; if (yy < 0 || yy >= size) continue;
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx; if (xx < 0 || xx >= size) continue;
                                    votes[cls[yy * size + xx]]++;
                                }
                            }
                            int best = cls[y * size + x];
                            for (int k = 0; k < Classes; k++) if (votes[k] > votes[best]) best = k;
                            smooth[y * size + x] = (byte)best;
                        }
                    var rgba = new byte[size * size * 4];
                    for (int i = 0; i < smooth.Length; i++)
                    {
                        Color32 c = smooth[i] == 0 ? Water : Albedo(BiomeAt(smooth[i] - 1));
                        int o = i * 4;
                        rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = 255;
                    }
                    var bytes = ImageConv.EncodeRgbaToPNG(rgba, size, size);
                    png = bytes;
                    File.WriteAllBytes(Path.Combine(worldDataPath, FileName), bytes);
                    ZLog.Log($"WebMap: chart built, {bytes.Length} bytes");
                }
                catch (Exception e) { ZLog.LogWarning("WebMap: chart not written: " + e.Message); }
                finally { building = false; }
            });
        }

        // Heightmap.Biome is a bit flag; the chart wants a small dense index.
        private const int Classes = 1 + 32;
        private static int Index(Heightmap.Biome b)
        {
            int v = (int)b, i = 0;
            while (v > 1 && i < 31) { v >>= 1; i++; }
            return i;
        }
        private static Heightmap.Biome BiomeAt(int index) => (Heightmap.Biome)(1 << index);

        public static byte[] GetPng() => png ?? new byte[0];
    }
}
