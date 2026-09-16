using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WebMap
{
    // The projection is deliberately independent of a table ZDO.  A copied pin in
    // two tables therefore has one stable web-map identity, not two transient IDs.
    internal sealed class CartographyPin
    {
        public string Name;
        public Vector3 Position;
        public int Type;
        public bool Checked;
    }

    internal sealed class CartographyWebPin
    {
        public string Id;
        public string Name;
        public string Type;
        public float X;
        public float Z;
        public bool Checked;
        public string Text;
    }

    internal static class CartographyPinProjection
    {
        internal static IReadOnlyList<CartographyWebPin> Project(IEnumerable<CartographyPin> source)
        {
            var pins = new Dictionary<string, CartographyWebPin>(StringComparer.Ordinal);
            foreach (var pin in source ?? Enumerable.Empty<CartographyPin>())
            {
                if (pin == null || !IsFinite(pin.Position.x) || !IsFinite(pin.Position.z)) continue;
                string sourceName = pin.Name ?? string.Empty;
                string name = Clean(sourceName);
                // Length-prefix the unmodified label so CSV display sanitizing
                // cannot collapse two distinct vanilla labels into one identity.
                // Checked is not part of the key: a ticked pin is the same pin
                // unticked later, and copies of one find agree on it anyway.
                string key = pin.Type.ToString(CultureInfo.InvariantCulture) + "\n" +
                    sourceName.Length.ToString(CultureInfo.InvariantCulture) + ":" + sourceName + "\n" +
                    // "R" is a round-trippable representation. Do not use the
                    // display precision here: distinct pins a few centimetres
                    // apart are still distinct cartography-table pins.
                    pin.Position.x.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                    pin.Position.z.ToString("R", CultureInfo.InvariantCulture);
                string id = "cartography:" + Hash(key);
                pins[id] = new CartographyWebPin
                {
                    Id = id,
                    Name = name,
                    Type = WebType(pin.Type),
                    X = pin.Position.x,
                    Z = pin.Position.z,
                    Checked = pin.Checked,
                    Text = name
                };
            }
            return MergeByRadius(pins.Values.OrderBy(pin => pin.Id, StringComparer.Ordinal).ToList());
        }

        // The exact-hash pass only removes bit-identical copies. A pin dragged and
        // re-dropped in a second table lands a few centimetres away and survives
        // it; this merges what is obviously the same find. Grouped by web type so
        // a mine is never swallowed by a nearby cave, and walking in stable
        // (ordinal-Id) order so the survivor is deterministic. The pin count per
        // table is a few hundred at most, which makes the naive pairwise sweep
        // per group cheaper than any spatial index.
        private static List<CartographyWebPin> MergeByRadius(List<CartographyWebPin> ordered)
        {
            float radius = WebMapConfig.CARTOGRAPHY_DEDUP_RADIUS_METERS;
            if (radius <= 0f) return ordered;    // exact-hash dedup only
            float r2 = radius * radius;
            var groups = new Dictionary<string, List<CartographyWebPin>>(StringComparer.Ordinal);
            var result = new List<CartographyWebPin>(ordered.Count);
            foreach (var pin in ordered)
            {
                if (!groups.TryGetValue(pin.Type, out var group))
                    groups[pin.Type] = group = new List<CartographyWebPin>();
                CartographyWebPin survivor = null;
                for (int i = 0; i < group.Count; i++)
                {
                    var kept = group[i];
                    float dx = kept.X - pin.X, dz = kept.Z - pin.Z;
                    if (dx * dx + dz * dz <= r2) { survivor = kept; break; }
                }
                if (survivor != null)
                {
                    // A ticked twin ticks the pin it merges into.
                    if (pin.Checked) survivor.Checked = true;
                    continue;
                }
                group.Add(pin);
                result.Add(pin);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        internal static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace(",", " ").Trim();
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
            }
        }

        // WebMap v2.9.0 has five sprite classes.  Unknown vanilla icons remain
        // visible as dots instead of producing an invisible CSS class.
        private static string WebType(int type)
        {
            switch (type)
            {
                case 0: return "fire";
                case 1: return "house";
                case 2: return "mine";
                case 3: return "cave";
                default: return "dot";
            }
        }
    }

    internal static class CartographyTablePins
    {
        private const string TablePrefab = "piece_cartographytable";
        private const int MaxExploredCells = 16 * 1024 * 1024;
        private static bool started;
        // Table state is deliberately keyed separately: a bad write in one ZDO
        // must not freeze updates from every other cartography table.
        private static readonly Dictionary<string, IReadOnlyList<CartographyPin>> lastGoodByTable =
            new Dictionary<string, IReadOnlyList<CartographyPin>>(StringComparer.Ordinal);

        internal static void Start()
        {
            if (started) return;
            started = true;
            StaticCoroutine.Start(RefreshLoop());
        }

        internal static IEnumerator RefreshLoop()
        {
            while (true)
            {
                Refresh();
                yield return new WaitForSeconds(Math.Max(1f, WebMapConfig.CARTOGRAPHY_PIN_UPDATE_INTERVAL));
            }
        }

        // A corrupt table contributes its own last-good pins while healthy tables
        // still refresh. A table which no longer exists is removed from the cache.
        internal static bool Refresh()
        {
            if (WebMap.mapDataServer == null) return false;
            // Online can be reached before the ZDO manager is populated on some
            // dedicated-server startup paths. Preserve the currently published
            // pins until a complete table snapshot can be read.
            if (ZDOMan.instance == null) return false;
            if (!WebMapConfig.IMPORT_CARTOGRAPHY_PINS)
            {
                WebMap.mapDataServer.ReplaceCartographyPins(Enumerable.Empty<CartographyWebPin>());
                return true;
            }
            try
            {
                var sw = Stopwatch.StartNew();
                var tables = new List<ZDO>();
                int scanIndex = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(TablePrefab, tables, ref scanIndex))
                {
                    // The game API scans at most 400 sectors per call. Continue in
                    // this main-thread refresh until it has also visited the global
                    // and portal collections and reports a complete snapshot.
                }
                var allPins = new List<CartographyPin>();
                var presentTables = new HashSet<string>(StringComparer.Ordinal);
                foreach (var table in tables)
                {
                    string tableId = table.m_uid.ToString();
                    presentTables.Add(tableId);
                    byte[] compressed = table.GetByteArray(ZDOVars.s_data, null);
                    if (compressed == null || compressed.Length == 0)
                    {
                        lastGoodByTable.Remove(tableId);
                        continue;
                    }
                    try
                    {
                        lastGoodByTable[tableId] = ReadTable(compressed).ToList();
                    }
                    catch (Exception e)
                    {
                        ZLog.LogWarning("WebMap: unable to decode cartography table " + tableId + "; retaining its last-good pins. " + e.Message);
                    }
                    IReadOnlyList<CartographyPin> pins;
                    if (lastGoodByTable.TryGetValue(tableId, out pins)) allPins.AddRange(pins);
                }
                foreach (var staleTable in lastGoodByTable.Keys.Where(key => !presentTables.Contains(key)).ToList())
                    lastGoodByTable.Remove(staleTable);
                var projected = CartographyPinProjection.Project(allPins);
                WebMap.mapDataServer.ReplaceCartographyPins(projected);
                if (WebMapConfig.DEBUG)
                    ZLog.Log($"WebMap: cartography: {tables.Count} tables, {projected.Count} unique pins in {sw.Elapsed.TotalMilliseconds:0.#} ms");
                return true;
            }
            catch (Exception e)
            {
                ZLog.LogWarning("WebMap: cartography table scan failed; retaining last-good pins. " + e.Message);
                return false;
            }
        }

        // Vanilla MapTable stores compressed shared-map data: version, explored
        // count + flags, then owner/name/position/type/checked/author pins.
        private static IEnumerable<CartographyPin> ReadTable(byte[] compressed)
        {
            var package = new ZPackage(Utils.Decompress(compressed));
            int version = package.ReadInt();
            if (version < 1 || version > 10) throw new InvalidOperationException("unsupported MapTable data version " + version);
            int exploredCount = package.ReadInt();
            if (exploredCount < 0 || exploredCount > MaxExploredCells)
                throw new InvalidOperationException("invalid MapTable explored count " + exploredCount);
            for (var i = 0; i < exploredCount; i++) package.ReadBool();
            int count = package.ReadInt();
            if (count < 0 || count > 100000) throw new InvalidOperationException("invalid MapTable pin count " + count);
            var result = new List<CartographyPin>(count);
            for (var i = 0; i < count; i++)
            {
                package.ReadLong(); // owner ID
                var pin = new CartographyPin { Name = package.ReadString(), Position = package.ReadVector3(), Type = package.ReadInt() };
                pin.Checked = package.ReadBool();
                // Current versions write PlatformUserID after checked.  It is not
                // part of identity and is intentionally not exposed on the web.
                if (version >= 3) package.ReadString();
                result.Add(pin);
            }
            return result;
        }
    }
}
