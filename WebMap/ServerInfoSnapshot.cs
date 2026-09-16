using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;

namespace WebMap
{
    // Intentionally a small, public projection rather than a dump of BepInEx
    // configuration. The catalog lists the mods that are part of the deployed
    // system (plus explicitly-disabled ones); configuration is exposed only
    // through the per-mod allowlists below. Unknown keys stay private.
    internal static class ServerInfoSnapshot
    {
        internal const string EmptyJson = "{\"server\":{\"gameVersion\":\"\",\"worldName\":\"\",\"playerCount\":0,\"serverName\":\"\",\"visibility\":\"\",\"worldModifiers\":[]},\"mods\":[],\"updatedAt\":\"\"}";

        private sealed class CatalogEntry
        {
            internal readonly string Id;
            internal readonly string Name;
            internal readonly string Url;
            internal readonly string Description;

            internal CatalogEntry(string id, string name, string url, string description)
            {
                Id = id;
                Name = name;
                Url = url;
                Description = description;
            }
        }

        // Mods that are deployed and loaded by this server. Names match the
        // loaded BepInEx plugin names so status/version resolve from the registry.
        private static readonly CatalogEntry[] Catalog = {
            new CatalogEntry("webmap", "WebMap", "https://thunderstore.io/c/valheim/p/ArmchairSavages/WebMap/", "Mapa del mundo para consultar desde el navegador."),
            new CatalogEntry("server-devcommands", "Server Devcommands", "https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/", "Comandos de desarrollo y permisos por personaje en el servidor."),
            new CatalogEntry("valheim-tune", "ValheimTune", "https://thunderstore.io/c/valheim/p/Akoozie/ValheimTune/", "Ajustes de rendimiento, red y guardado del servidor."),
            new CatalogEntry("vpo", "Valheim Performance Optimizations", "https://thunderstore.io/c/valheim/p/ontrigger/ValheimPerformanceOptimizations/", "Optimizaciones de logica y carga solo para el servidor: streaming de objetos, terreno, fisicas y GC. En Linux los jobs Burst nativos estan desactivados (rutas managed activas) y el baking de colisiones en hilo esta desactivado por ser experimental."),
            new CatalogEntry("tickprofiler", "TickProfiler", "https://thunderstore.io/c/valheim/p/MagiCorp/TickProfiler/", "Profiler de ticks solo para el servidor: tasa de tick efectiva, stalls de hilo principal, CPU, memoria/GC, red, replicacion ZDO, IA, drops, spawning, terreno y metodos de otros mods. El overlay de cliente es opcional y no se exige a los jugadores."),
            new CatalogEntry("serverside-qol", "ServersideQoL", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL/", "Nucleo de las mejoras ServersideQoL (patcher de preloader)."),
            new CatalogEntry("just-sleep", "ServersideQoL.JustSleep", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_JustSleep/", "Permite dormir con un porcentaje configurable de jugadores en camas."),
            new CatalogEntry("signs", "ServersideQoL.Signs", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_Signs/", "Mejora el comportamiento de los carteles desde el servidor."),
            new CatalogEntry("multiplayer-tweaks", "ServersideQoL.MultiplayerTweaks", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_MultiplayerTweaks/", "Ajustes de calidad de vida para multijugador."),
            new CatalogEntry("auto-map-tables", "ServersideQoL.AutoMapTables", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_AutoMapTables/", "Automatiza la integracion de exploracion mediante mesas cartograficas."),
            new CatalogEntry("auto-store", "ServersideQoL.AutoStore", "https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_AutoStore/", "Almacena u ordena objetos cercanos automaticamente cuando se habilita."),
            new CatalogEntry("server-password-once", "ServerPasswordOnce", "https://thunderstore.io/c/valheim/p/DooDesch/ServerPasswordOnce/", "Evita repetir la contrasena al volver, salvo que haya cambiado."),
            new CatalogEntry("render-limits", "Render Limits", "https://github.com/JereKuusela/valheim-render_limits", "Incompatible con Valheim 1.0.12; cuarentenado y no cargado."),
        };

        // Allowlisted configuration keys per mod, verified against the generated
        // config files. Values are read live from BepInEx/config.
        private static readonly string[,] VtAllow = {
            { "Compat", "KnownGoodBuilds" }, { "Compat", "DisableOnUnknownBuild" },
            { "Server", "TargetFrameRate" }, { "Server", "SkipRenderMesh" }, { "Server", "DeferAssetUnload" },
            { "Sync", "SendWindowBytes" }, { "Sync", "MinHeadroomBytes" }, { "Sync", "AllPeersPerRound" },
            { "Sync", "RelayMinIntervalMs" }, { "Sync", "DirtySets" },
            { "Steam", "SendRateMaxBytesPerSec" }, { "Steam", "OverrideSendRate" },
            { "Cleanup", "FloatingDropsDelete" },
        };
        private static readonly string[,] QolCoreAllow = { { "General", "Enabled" }, { "General", "FarMessageRange" } };
        private static readonly string[,] JustSleepAllow = { { "JustSleep", "Enabled" }, { "JustSleep", "MinPlayersInBed" }, { "JustSleep", "RequiredPlayerPercentage" } };
        private static readonly string[,] SignsAllow = { { "Signs", "Enabled" }, { "Signs", "TimeSigns" } };
        private static readonly string[,] MtAllow = {
            { "MultiplayerTweaks", "Enabled" }, { "MultiplayerTweaks", "ForcePlayerMapPin" },
            { "MultiplayerTweaks", "AssignInteractablesToClosestPlayer" }, { "MultiplayerTweaks", "AssignMobsToClosestPlayer" },
            { "MultiplayerTweaks", "AssignShipsToCaptain" },
        };
        private static readonly string[,] AmtAllow = { { "AutoMapTables", "Enabled" }, { "AutoMapTables", "MapTableRange" } };
        private static readonly string[,] AsAllow = { { "AutoStore", "Enabled" }, { "AutoStore", "AutoSort" }, { "AutoStore", "AutoPickup" }, { "AutoStore", "AutoPickupRange" } };
        private static readonly string[,] SpoAllow = { { "General", "Enabled" }, { "Guests", "MaxGuests" }, { "Guests", "ForgetAfterDays" } };

        private static readonly string[,] VpoAllow = { { "General", "Max physics updates per frame" }, { "General", "Reuse collision callbacks" }, { "General", "Threaded terrain collision baking enabled" } };
        private static readonly string[,] TickAllow = { { "General", "Enabled" }, { "General", "ReportIntervalSeconds" }, { "General", "TopConsumers" }, { "Output", "SpikeThresholdMs" }, { "Profiling", "DeepGameProfiling" }, { "Profiling", "ProfileModHarmonyMethods" }, { "Profiling", "MainThreadStallThresholdMs" }, { "Notifications", "AnnounceHitchesInChat" } };

        private static string CfgFileFor(string id)
        {
            switch (id)
            {
                case "valheim-tune": return "akoozie.valheimtune.cfg";
                case "vpo": return "dev.ontrigger.vpo.cfg";
                case "tickprofiler": return "magicorp.valheim.tickprofiler.cfg";
                case "serverside-qol": return "ArgusMagnus.ServersideQoL.cfg";
                case "just-sleep": return "ArgusMagnus.ServersideQoL.JustSleep.cfg";
                case "signs": return "ArgusMagnus.ServersideQoL.Signs.cfg";
                case "multiplayer-tweaks": return "ArgusMagnus.ServersideQoL.MultiplayerTweaks.cfg";
                case "auto-map-tables": return "ArgusMagnus.ServersideQoL.AutoMapTables.cfg";
                case "auto-store": return "ArgusMagnus.ServersideQoL.AutoStore.cfg";
                case "server-password-once": return "DooDesch.ServerPasswordOnce.cfg";
            }
            return null;
        }

        private static string[,] AllowFor(string id)
        {
            switch (id)
            {
                case "valheim-tune": return VtAllow;
                case "vpo": return VpoAllow;
                case "tickprofiler": return TickAllow;
                case "serverside-qol": return QolCoreAllow;
                case "just-sleep": return JustSleepAllow;
                case "signs": return SignsAllow;
                case "multiplayer-tweaks": return MtAllow;
                case "auto-map-tables": return AmtAllow;
                case "auto-store": return AsAllow;
                case "server-password-once": return SpoAllow;
            }
            return null;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> cfgCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime cfgCacheAt = DateTime.MinValue;

        private static Dictionary<string, string> ReadCfg(string file)
        {
            bool stale = (DateTime.UtcNow - cfgCacheAt).TotalSeconds >= 60;
            if (stale) { cfgCache.Clear(); cfgCacheAt = DateTime.UtcNow; }
            Dictionary<string, string> cached;
            if (!stale && cfgCache.TryGetValue(file, out cached)) return cached;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(Paths.ConfigPath, file);
                if (File.Exists(path))
                {
                    string section = "";
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        if (line[0] == '[' && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        result[section + "|" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                ZLog.LogWarning("WebMap: cannot read " + file + " for server info: " + ex.Message);
            }
            cfgCache[file] = result;
            return result;
        }

        private static void AppendFileConfig(StringBuilder json, string file, string[,] allow)
        {
            Dictionary<string, string> cfg = ReadCfg(file);
            json.Append('{');
            bool first = true;
            for (int i = 0; i < allow.GetLength(0); i++)
            {
                string key = allow[i, 1];
                string val;
                if (!cfg.TryGetValue(allow[i, 0] + "|" + key, out val)) continue;
                if (!first) json.Append(',');
                first = false;
                json.Append("\"").Append(Escape(key)).Append("\":\"").Append(Escape(val)).Append("\"");
            }
            json.Append('}');
        }

        internal static string BuildJson(string worldName, int playerCount)
        {
            var plugins = new List<PluginInfo>();
            bool registryAvailable = TryGetPlugins(plugins);
            var json = new StringBuilder();
            json.Append("{\"server\":{");
            json.Append("\"gameVersion\":\"").Append(Escape(GameVersion())).Append("\"");
            json.Append(",\"worldName\":\"").Append(Escape(worldName)).Append("\"");
            json.Append(",\"playerCount\":").Append(Math.Max(0, playerCount).ToString(CultureInfo.InvariantCulture));
            json.Append(",\"serverName\":\"").Append(Escape(ServerName())).Append("\"");
            json.Append(",\"visibility\":\"").Append(Escape(Visibility())).Append("\"");
            json.Append(",\"worldModifiers\":");
            AppendWorldModifiers(json);
            json.Append("},\"mods\":[");
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (i > 0) json.Append(',');
                AppendMod(json, Catalog[i], plugins, registryAvailable);
            }
            json.Append("],\"updatedAt\":\"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\"}");
            return json.ToString();
        }

        private static bool TryGetPlugins(List<PluginInfo> result)
        {
            try
            {
                foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
                    result.Add(plugin);
                return true;
            }
            catch (Exception ex)
            {
                ZLog.LogWarning("WebMap: cannot read the BepInEx plugin registry for server info: " + ex.Message);
                return false;
            }
        }

        private static void AppendMod(StringBuilder json, CatalogEntry entry, List<PluginInfo> plugins, bool registryAvailable)
        {
            PluginInfo plugin = FindPlugin(entry, plugins);
            string status;
            string version = null;
            if (entry.Id == "webmap")
            {
                status = "enabled";
                version = WebMap.VERSION;
            }
            else if (entry.Id == "render-limits")
            {
                status = "disabled";
            }
            else if (!registryAvailable)
            {
                status = "error";
            }
            else if (plugin != null)
            {
                status = plugin.Instance != null ? "enabled" : "available";
                version = plugin.Metadata.Version != null ? plugin.Metadata.Version.ToString() : null;
            }
            else
            {
                status = "available";
            }

            json.Append("{\"id\":\"").Append(Escape(entry.Id)).Append("\"");
            json.Append(",\"name\":\"").Append(Escape(entry.Name)).Append("\"");
            json.Append(",\"version\":");
            if (version == null) json.Append("null"); else json.Append("\"").Append(Escape(version)).Append("\"");
            json.Append(",\"status\":\"").Append(status).Append("\"");
            json.Append(",\"url\":\"").Append(Escape(entry.Url)).Append("\"");
            json.Append(",\"description\":\"").Append(Escape(entry.Description)).Append("\"");
            json.Append(",\"config\":");
            AppendPublicConfig(json, entry.Id);
            json.Append('}');
        }

        private static PluginInfo FindPlugin(CatalogEntry entry, List<PluginInfo> plugins)
        {
            foreach (PluginInfo plugin in plugins)
            {
                if (plugin == null || plugin.Metadata == null) continue;
                string name = plugin.Metadata.Name ?? "";
                string guid = plugin.Metadata.GUID ?? "";
                if (string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Normalize(name), Normalize(entry.Id), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Normalize(guid), Normalize(entry.Id), StringComparison.OrdinalIgnoreCase))
                    return plugin;
            }
            return null;
        }

        private static void AppendWebMapConfig(StringBuilder json)
        {
            json.Append("{\"alwaysMap\":").Append(WebMapConfig.ALWAYS_MAP ? "true" : "false");
            json.Append(",\"alwaysVisible\":").Append(WebMapConfig.ALWAYS_VISIBLE ? "true" : "false");
            json.Append(",\"showVehicles\":").Append(WebMapConfig.SHOW_VEHICLES ? "true" : "false");
            json.Append(",\"importCartographyPins\":").Append(WebMapConfig.IMPORT_CARTOGRAPHY_PINS ? "true" : "false");
            json.Append('}');
        }

        private static void AppendPublicConfig(StringBuilder json, string id)
        {
            if (id == "webmap") { AppendWebMapConfig(json); return; }
            string file = CfgFileFor(id);
            string[,] allow = AllowFor(id);
            if (file != null && allow != null) { AppendFileConfig(json, file, allow); return; }
            json.Append("{}");
        }

        private static void AppendWorldModifiers(StringBuilder json)
        {
            json.Append('[');
            bool first = true;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != "-modifier") continue;
                    if (i + 2 >= args.Length) break;
                    string type = args[i + 1] ?? "";
                    string value = args[i + 2] ?? "";
                    i += 2;
                    if (!first) json.Append(',');
                    first = false;
                    json.Append("{\"id\":\"").Append(Escape(type)).Append("\"");
                    json.Append(",\"value\":\"").Append(Escape(value)).Append("\"");
                    json.Append(",\"label\":\"").Append(Escape(ModifierLabel(type, value))).Append("\"}");
                }
            }
            catch (Exception ex)
            {
                ZLog.LogWarning("WebMap: cannot read world modifiers for server info: " + ex.Message);
            }
            json.Append(']');
        }

        private static string ModifierLabel(string type, string value)
        {
            if (string.Equals(type, "resources", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value, "muchmore", StringComparison.OrdinalIgnoreCase))
                return "Recursos x2";
            return (type + " " + value).Trim();
        }

        private static string CmdValue(string flag)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == flag) return args[i + 1] ?? "";
            }
            catch { }
            return "";
        }

        private static string ServerName() { return CmdValue("-name"); }

        private static string Visibility()
        {
            string p = CmdValue("-public");
            if (p == "1") return "public";
            if (p == "0") return "private";
            return "";
        }

        private static string GameVersion()
        {
            try { return global::Version.GetVersionString() ?? ""; }
            catch { return ""; }
        }

        private static string Normalize(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "") if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
            return result.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        }
    }
}