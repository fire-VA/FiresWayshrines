using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>One coordinate teleport destination (Marketplace Teleporter schema).</summary>
    public sealed class TeleporterPin
    {
        public string Name;
        public string Sprite = "none";
        public int X, Y, Z;
        public int Speed;            // travel-anim hint; ≤0 = instant
        public string FromProfile;   // set only for "@from:" imports
        public Vector3 Pos => new Vector3(X, Y, Z);
    }

    /// <summary>
    /// Admin-defined coordinate teleport hubs — the Marketplace Teleporter model layered onto the
    /// Wayshrine backend so an NPC "Teleporter" can offer fixed destinations that aren't placed
    /// shrines. Sources merged (Fires wins): MP drop-in Configs/Teleporters/*.cfg + Fires
    /// FiresWayshrines/Teleporters/*.cfg. Grammar (schema: FiresNPCs marketplace-schemas/Teleporter):
    ///   [profile]                         hub name (brackets stripped, spaces removed, lowercased)
    ///   Name&lt;speed=N&gt;,x,y,z[,sprite]       a pin — only coord/sprite tokens are space-stripped
    ///   @from:otherhub                    import another hub's pins
    /// Hot-reloads on a 5s mtime check. Teleport execution reuses WayshrineTeleportFlow (ore/NoPortals/
    /// boss validation), so hub jumps obey the same transport rules as shrine jumps — one travel system.
    /// </summary>
    public static class TeleporterHubConfig
    {
        private static readonly Dictionary<string, List<TeleporterPin>> _profiles =
            new Dictionary<string, List<TeleporterPin>>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _seenWrite = DateTime.MinValue;

        public static IEnumerable<string> ProfileNames { get { EnsureLoaded(); return _profiles.Keys; } }

        /// <summary>Resolved pins for a profile — its own pins plus any "@from" imports (cycle-safe).</summary>
        public static List<TeleporterPin> GetPins(string profile)
        {
            EnsureLoaded();
            var result = new List<TeleporterPin>();
            if (!string.IsNullOrEmpty(profile))
                Collect(profile.Trim().ToLowerInvariant(), result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>Find a pin by display name across all hubs (for TeleportToNamed).</summary>
        public static TeleporterPin FindPinByName(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var list in _profiles.Values)
                foreach (var p in list)
                    if (p.FromProfile == null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        return p;
            return null;
        }

        private static void Collect(string profile, List<TeleporterPin> into, HashSet<string> seen)
        {
            if (!seen.Add(profile) || !_profiles.TryGetValue(profile, out var pins)) return;
            foreach (var p in pins)
            {
                if (!string.IsNullOrEmpty(p.FromProfile)) Collect(p.FromProfile, into, seen);
                else into.Add(p);
            }
        }

        public static void Reload() { _loaded = false; EnsureLoaded(); }

        private static IEnumerable<string> Folders()
        {
            yield return Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", "Configs", "Teleporters");
            yield return Path.Combine(BepInEx.Paths.ConfigPath, "FiresWayshrines", "Teleporters");
        }

        private static void EnsureLoaded()
        {
            if (_loaded && Time.unscaledTime >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.unscaledTime + 5f;
                if (NewestWrite() > _seenWrite) _loaded = false;
            }
            if (_loaded) return;
            _loaded = true;
            _nextStaleCheck = Time.unscaledTime + 5f;
            _profiles.Clear();
            SeedExample();

            foreach (string folder in Folders())
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
                {
                    try { ParseFile(file); }
                    catch (Exception ex) { Debug.LogWarning($"[Teleporter] parse failed for {Path.GetFileName(file)}: {ex.Message}"); }
                }
            }
            _seenWrite = NewestWrite();
            if (_profiles.Count > 0) Debug.Log($"[Teleporter] loaded {_profiles.Count} hub profile(s)");
        }

        // First-run template so admins see the format (Fires folder only; never overwrites).
        private static void SeedExample()
        {
            try
            {
                string folder = Path.Combine(BepInEx.Paths.ConfigPath, "FiresWayshrines", "Teleporters");
                if (Directory.Exists(folder)) return;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "example.cfg"), string.Join("\n", new[]
                {
                    "# Teleport hubs for the NPC Teleporter (dialogue OpenUI,Teleporter,<hub>).",
                    "# Drop a Marketplace Configs/Teleporters/*.cfg in and it loads here unchanged.",
                    "",
                    "[spawn_hub]",
                    "# Name,x,y,z[,sprite]  — name may contain spaces; <speed=N> optional",
                    "Meadows Spawn,0,30,0",
                    "Black Forest Edge,300,32,-450",
                }));
            }
            catch { }
        }

        private static DateTime NewestWrite()
        {
            DateTime newest = DateTime.MinValue;
            foreach (string folder in Folders())
                try { if (Directory.Exists(folder)) foreach (var f in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories)) { var t = File.GetLastWriteTimeUtc(f); if (t > newest) newest = t; } }
                catch { }
            return newest;
        }

        private static void ParseFile(string file)
        {
            string key = "default";
            List<TeleporterPin> cur = null;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Replace(" ", "").ToLowerInvariant();
                    if (!_profiles.TryGetValue(key, out cur)) { cur = new List<TeleporterPin>(); _profiles[key] = cur; }
                    continue;
                }
                if (cur == null && !_profiles.TryGetValue(key, out cur)) { cur = new List<TeleporterPin>(); _profiles[key] = cur; }

                if (line.StartsWith("@from:", StringComparison.OrdinalIgnoreCase))
                {
                    cur.Add(new TeleporterPin { FromProfile = line.Substring("@from:".Length).Replace(" ", "").ToLowerInvariant() });
                    continue;
                }
                var pin = ParsePin(line);
                if (pin != null) cur.Add(pin);
            }
        }

        private static TeleporterPin ParsePin(string line)
        {
            var tokens = line.Split(',');
            if (tokens.Length < 4) return null;

            string name = tokens[0];
            int speed = 0;
            int tagStart = name.IndexOf("<speed=", StringComparison.OrdinalIgnoreCase);
            if (tagStart >= 0)
            {
                int tagEnd = name.IndexOf('>', tagStart);
                if (tagEnd > tagStart)
                {
                    string inner = name.Substring(tagStart + 7, tagEnd - tagStart - 7);
                    int.TryParse(inner.Trim(), out speed);
                    name = name.Remove(tagStart, tagEnd - tagStart + 1);
                }
            }
            name = name.Trim();

            if (!int.TryParse(tokens[1].Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) return null;
            if (!int.TryParse(tokens[2].Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return null;
            if (!int.TryParse(tokens[3].Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) return null;
            string sprite = tokens.Length >= 5 ? tokens[4].Replace(" ", "") : "none";

            return new TeleporterPin { Name = name, X = x, Y = y, Z = z, Speed = speed, Sprite = sprite };
        }
    }
}
