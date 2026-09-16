using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace WebMap
{
    // Boats and carts, reported live at /vehicles.
    //
    // Both are player-crafted, so they carry a creator on their ZDO and the
    // structures sweep used to paint them -- an anonymous brown dot in the
    // middle of the ocean. They are not buildings: they move, and what anyone
    // wants from them is "where did I leave it", which wants a marker, a name
    // and a heading rather than a pixel.
    //
    // Classified by component rather than prefab name, so a boat added in a
    // later patch is a boat without this needing to learn its name.
    //
    // Two coroutines share the cache: discovery walks the world by prefab every
    // minute to learn new vehicle ids, the update loop touches only the cached
    // ids every couple of seconds to keep positions fresh. Both run on the main
    // thread; HTTP threads only ever read the volatile JSON snapshot.
    internal static class Vehicles
    {
        internal enum Kind { None, Boat, Cart }

        private struct Entry { public Kind kind; public string name; public float x, y, z, yaw; }

        private static readonly Dictionary<int, Kind> kindCache = new Dictionary<int, Kind>();
        private static readonly Dictionary<int, string> nameCache = new Dictionary<int, string>();

        // Seed of the vanilla vehicle prefabs. GetAllZDOsWithPrefabIterative is
        // name-driven, so this is a name set, not a hash set; Classify() adds any
        // other prefab it has seen prove it carries a Ship or a Vagon, which lets
        // a modded or later-patch vehicle enter discovery the first time it is
        // seen through any other path.
        private static readonly HashSet<string> vehiclePrefabNames = new HashSet<string>
        {
            "Raft", "Karve", "VikingShip", "Cart",
        };

        // Known vehicles by ZDO id. Entries are value copies; the update loop
        // re-reads the live ZDO each cycle and drops ids that went away.
        private static readonly Dictionary<ZDOID, Entry> known = new Dictionary<ZDOID, Entry>();
        private static readonly List<ZDOID> snapshotIds = new List<ZDOID>();
        private static readonly List<ZDOID> removals = new List<ZDOID>();
        private static readonly List<ZDO> scanBuffer = new List<ZDO>();
        private static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);

        private const string EmptyJson = "{\"vehicles\":[],\"boats\":0,\"carts\":0}";
        // Written on the main thread, read by HTTP threads: only ever swapped for
        // a complete immutable string, same pattern as the player snapshot.
        private static volatile string json = EmptyJson;

        private static bool started;

        public static void Start()
        {
            if (started) return;
            started = true;
            StaticCoroutine.Start(DiscoveryLoop());
            StaticCoroutine.Start(UpdateLoop());
        }

        public static Kind Classify(int prefabHash)
        {
            if (kindCache.TryGetValue(prefabHash, out var cached)) return cached;
            Kind k = Kind.None;
            string label = null;
            try
            {
                var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabHash) : null;
                if (go != null)
                {
                    if (go.GetComponent<Ship>() != null) k = Kind.Boat;
                    else if (go.GetComponent<Vagon>() != null) k = Kind.Cart;
                    if (k != Kind.None)
                    {
                        label = go.name;
                        vehiclePrefabNames.Add(label);   // teach discovery the new name
                    }
                }
            }
            catch { }
            kindCache[prefabHash] = k;
            nameCache[prefabHash] = label ?? "";
            return k;
        }

        // The slow pass. Learn about new and destroyed vehicles; positions are
        // handled by the fast pass.
        private static IEnumerator DiscoveryLoop()
        {
            yield return new WaitForSeconds(5f);          // let the world finish loading
            while (true)
            {
                Discover();
                yield return new WaitForSeconds(Mathf.Max(15f, WebMapConfig.VEHICLE_DISCOVERY_INTERVAL));
            }
        }

        private static void Discover()
        {
            if (ZDOMan.instance == null) return;
            var sw = Stopwatch.StartNew();
            // Copy first: Classify below can add to the set while we walk it.
            var names = new List<string>(vehiclePrefabNames);
            for (int n = 0; n < names.Count; n++)
            {
                scanBuffer.Clear();
                int scanIndex = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(names[n], scanBuffer, ref scanIndex))
                {
                    // The game API scans at most 400 sectors per call. Drive it to
                    // completion here, on the main thread, once a minute.
                }
                for (int i = 0; i < scanBuffer.Count; i++)
                {
                    var zdo = scanBuffer[i];
                    if (zdo == null) continue;
                    int pref;
                    try { pref = zdo.GetPrefab(); } catch { continue; }
                    Kind kind = Classify(pref);
                    if (kind == Kind.None) continue;      // a mod may reuse these names
                    nameCache.TryGetValue(pref, out string name);
                    Vector3 pos = zdo.GetPosition();
                    Vector3 euler = zdo.GetRotation().eulerAngles;
                    known[zdo.m_uid] = new Entry
                    {
                        kind = kind, name = name ?? "",
                        x = pos.x, y = pos.y, z = pos.z, yaw = euler.y,
                    };
                }
            }
            if (WebMapConfig.DEBUG)
                ZLog.Log($"WebMap: vehicles scan: {known.Count} vehicles in {sw.Elapsed.TotalMilliseconds:0.#} ms");
        }

        // The fast pass: touches only the cached ids, never walks the world, and
        // rebuilds the served snapshot. Allocations stay near zero between cycles.
        private static IEnumerator UpdateLoop()
        {
            yield return new WaitForSeconds(1f);
            while (true)
            {
                Update();
                yield return new WaitForSeconds(Mathf.Max(0.5f, WebMapConfig.VEHICLE_UPDATE_INTERVAL));
            }
        }

        private static void Update()
        {
            if (!WebMapConfig.SHOW_VEHICLES) { known.Clear(); json = EmptyJson; return; }
            if (ZDOMan.instance == null) return;          // keep the last snapshot

            snapshotIds.Clear();
            foreach (var kv in known) snapshotIds.Add(kv.Key);

            int boats = 0, carts = 0;
            removals.Clear();
            sb.Clear();
            sb.Append("{\"vehicles\":[");
            bool first = true;
            for (int i = 0; i < snapshotIds.Count; i++)
            {
                var id = snapshotIds[i];
                Entry e = known[id];
                ZDO zdo = null;
                try { zdo = ZDOMan.instance.GetZDO(id); } catch { }
                if (zdo == null) { removals.Add(id); continue; }   // destroyed or de-spawned

                Vector3 pos = zdo.GetPosition();
                e.x = pos.x; e.y = pos.y; e.z = pos.z;
                e.yaw = zdo.GetRotation().eulerAngles.y;
                known[id] = e;

                if (e.kind == Kind.Boat && !WebMapConfig.SHOW_BOATS) continue;
                if (e.kind == Kind.Cart && !WebMapConfig.SHOW_CARTS) continue;
                if (!Explored(e.x, e.z)) continue;
                if (e.kind == Kind.Boat) boats++; else carts++;

                string kind = e.kind == Kind.Boat ? "boat" : "cart";
                string name = (e.name ?? "").Replace("\"", "");
                if (!first) sb.Append(",");
                first = false;
                sb.Append(System.FormattableString.Invariant(
                    $"{{\"id\":\"{id}\",\"kind\":\"{kind}\",\"type\":\"{name}\",\"name\":\"{name}\",\"x\":{e.x:0.#},\"y\":{e.y:0.#},\"z\":{e.z:0.#},\"rotation\":{e.yaw:0.#}}}"));
            }
            sb.Append("],\"boats\":").Append(boats).Append(",\"carts\":").Append(carts).Append("}");
            json = sb.ToString();

            for (int i = 0; i < removals.Count; i++) known.Remove(removals[i]);
        }

        // The overlay layers are drawn under the fog mask by the page, so builds in
        // unexplored land are hidden for free. Markers cannot be masked that way, so
        // the filtering happens here instead -- a boat somewhere nobody has been is
        // not reported at all, rather than merely not drawn.
        private static bool Explored(float x, float z)
        {
            try
            {
                var fog = WebMap.mapDataServer != null ? WebMap.mapDataServer.fogTexture : null;
                if (fog == null) return true;          // before the fog loads, hide nothing
                int size = WebMapConfig.TEXTURE_SIZE, half = size / 2;
                int px = Mathf.RoundToInt(x / WebMapConfig.PIXEL_SIZE + half);
                int py = Mathf.RoundToInt(z / WebMapConfig.PIXEL_SIZE + half);
                if (px < 0 || py < 0 || px >= size || py >= size) return false;
                return fog.GetPixel(px, py).r > 0.5f;  // white is explored
            }
            catch { return true; }
        }

        public static string GetJson() => json;
    }
}
