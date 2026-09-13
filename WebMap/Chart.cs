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
            string p = Path.Combine(worldDataPath, "chart.png");
            try { if (File.Exists(p)) png = File.ReadAllBytes(p); } catch { }
            if (png == null) StaticCoroutine.Start(Build(worldDataPath));
        }

        private static IEnumerator Build(string worldDataPath)
        {
            if (building) yield break;
            building = true;
            int size = WebMapConfig.TEXTURE_SIZE, half = size / 2;
            float ps = WebMapConfig.PIXEL_SIZE, halfPx = ps / 2f;
            var rgba = new byte[size * size * 4];
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
                    Color32 c = h < water ? Water : Albedo(biome);
                    int o = (y * size + x) * 4;
                    rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = 255;
                }
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var bytes = ImageConv.EncodeRgbaToPNG(rgba, size, size);
                    png = bytes;
                    File.WriteAllBytes(Path.Combine(worldDataPath, "chart.png"), bytes);
                    ZLog.Log($"WebMap: chart built, {bytes.Length} bytes");
                }
                catch (Exception e) { ZLog.LogWarning("WebMap: chart not written: " + e.Message); }
                finally { building = false; }
            });
        }

        public static byte[] GetPng() => png ?? new byte[0];
    }
}
