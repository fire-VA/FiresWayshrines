using System;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Hand-authored name pools per biome. The Wayshrine MonoBehaviour rolls a
    /// name on first Awake using a ZDO-seeded RNG so the same shrine always
    /// gets the same name across reloads, and the same shrine instance keeps
    /// it after a relog. Mirrors the
    /// <c>VerdantsAscent.Modules.Companions.WildSpawn.CompanionNamePool</c>
    /// pattern.
    /// </summary>
    internal static class WayshrineNamePool
    {
        public static string Roll(Heightmap.Biome biome, Random rng)
        {
            string[] pool = GetPool(biome);
            if (pool == null || pool.Length == 0) return "Wayshrine";
            return pool[rng.Next(pool.Length)];
        }

        /// <summary>
        /// Best-effort biome inference from the prefab name (e.g.
        /// "Wayshrine_meadows" -> Meadows). Falls back to Meadows.
        /// </summary>
        public static Heightmap.Biome InferBiomeFromName(string prefabOrPieceName)
        {
            if (string.IsNullOrEmpty(prefabOrPieceName)) return Heightmap.Biome.Meadows;
            string n = prefabOrPieceName.ToLowerInvariant();
            if (n.Contains("meadow"))     return Heightmap.Biome.Meadows;
            if (n.Contains("blackforest") || n.Contains("forest")) return Heightmap.Biome.BlackForest;
            if (n.Contains("swamp"))      return Heightmap.Biome.Swamp;
            if (n.Contains("mountain"))   return Heightmap.Biome.Mountain;
            if (n.Contains("plains"))     return Heightmap.Biome.Plains;
            if (n.Contains("mistland"))   return Heightmap.Biome.Mistlands;
            if (n.Contains("ashland"))    return Heightmap.Biome.AshLands;
            if (n.Contains("deepnorth") || n.Contains("frost")) return Heightmap.Biome.DeepNorth;
            if (n.Contains("ocean"))      return Heightmap.Biome.Ocean;
            return Heightmap.Biome.Meadows;
        }

        private static string[] GetPool(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:     return _meadows;
                case Heightmap.Biome.BlackForest: return _blackforest;
                case Heightmap.Biome.Swamp:       return _swamp;
                case Heightmap.Biome.Mountain:    return _mountain;
                case Heightmap.Biome.Plains:      return _plains;
                case Heightmap.Biome.Mistlands:   return _mistlands;
                case Heightmap.Biome.AshLands:    return _ashlands;
                case Heightmap.Biome.DeepNorth:   return _deepnorth;
                case Heightmap.Biome.Ocean:       return _ocean;
                default:                          return _meadows;
            }
        }

        // Meadows: pastoral, peaceful, Norse village.
        private static readonly string[] _meadows = new[]
        {
            "Sunhollow", "Greenmere", "Heartfell", "Brightmeadow", "Larkstone",
            "Honeyvale", "Wildflower Rest", "Goldfield", "Daisy Hollow", "Springwell",
            "Lambsgate", "Cloverhaven", "Farthing Glade", "Whisperbrook", "Willowmere",
            "Sunwarden", "Hearthlight", "Riverbough", "Old Oak Cross", "Apple Hollow",
            "Linden Stone", "Fairwind Pass", "Meadowsong", "Brightmoor", "Stagleap",
            "Bluebell Rest", "Hawthorn Vale", "Blackbird Hollow", "Honeycomb Glade",
            "Stillwater Crossing", "Three Sisters", "Old Mill Lane", "Cradlestone",
            "Greenwarden", "Sunstrider", "Moonberry Field", "Larkwell", "Pebblestream"
        };

        // Black Forest: ancient, mossy, half-haunted.
        private static readonly string[] _blackforest = new[]
        {
            "Mosshollow", "Ravenroot", "Greywood Sanctum", "Old Stone Barrow",
            "Whisperpine", "Dwarflantern", "Black Oak", "Twisted Hollow",
            "Wormwood Spire", "Thornwatch", "Greyfen Crossing", "Hollowstone",
            "Ironbark", "Crowsong", "Old King's Way", "Lichgate", "Briar Hollow",
            "Witchwood Arch", "Ashroot", "Doomspire", "Bonefall", "Ravensgate",
            "Splintershade", "Greypath", "Wraithwood", "Moss-Crowned Stone",
            "Owlmoor", "Boglight", "Witchstep", "Stagshade", "Whispering Cairn",
            "Fenpath", "Haldorn's Rest", "Black Cradle", "Forgotten Vale"
        };

        // Swamp: drowned, fungal, rotting.
        private static readonly string[] _swamp = new[]
        {
            "Drownreach", "Foulwater", "Sunken King's Throne", "Murkfen",
            "Leech Hollow", "Bogshroud", "Black Mire", "Rotwood Spire",
            "Hagsmoor", "Plaguepath", "Cursed Crossing", "Mire's Mouth",
            "Greyrot", "Sodden Stone", "Whisper-In-The-Reeds", "Carrion Cradle",
            "Dwell-Below", "Sinkroot", "Wraithmire", "Mawgate", "Moldhaven",
            "Slimeroot", "Sour Glade", "Drowned Saint", "Funguskeep", "Tarbough",
            "Vile Hollow", "Marshfeather", "Quagmother's Rest", "Stinkbark"
        };

        // Mountain: cold, austere, high stone.
        private static readonly string[] _mountain = new[]
        {
            "Skyreach", "Frostwarden", "Hailstone Pass", "Crowspire",
            "Stormcrown", "Granite Throne", "Iceveil", "Cloudbreak",
            "Wolfsong Peak", "Glaciergate", "Aerie", "Northwind Hold",
            "Stoneheart", "Drakespire", "Bitterstone", "Pinnaclewatch",
            "Frostmoot", "Iron Spire", "Heimdall's Watch", "Crag's End",
            "Whitepeak", "Bonefrost", "Stormwarden", "Howl's Pass",
            "Eagle's Ledge", "Snowmaiden", "Glimmerfrost", "Skybarrow",
            "Iceforge", "Bitter Crown"
        };

        // Plains: golden, harsh, ruin-strewn.
        private static readonly string[] _plains = new[]
        {
            "Suntomb", "Goldenshroud", "Lox Crossing", "Grassgate",
            "Bonemoor", "Rune-Sworn", "Harvestbreak", "Skylark Spire",
            "Oldstone Watch", "Wheatreach", "Goblin's Folly", "Sunwarden",
            "Tallgrass", "Three Crowns", "Rusted Helm", "Brassgate",
            "Last Furrow", "Dust-In-The-Wind", "Ironseed", "Stargrass",
            "Bone Cradle", "Lambsblade", "Goldcrown Rest", "Wandersfall",
            "Bramblewatch", "Rustspire", "Old Stalkers", "Sunwound",
            "Brightblood", "Hawkfall"
        };

        // Mistlands: alien, lichen, ancient ruin.
        private static readonly string[] _mistlands = new[]
        {
            "Veilreach", "Mistwarden", "Black Petal", "Lichen-Crowned",
            "Echo Hollow", "Glowveil", "Forgotten Choir", "Spire-In-The-Mist",
            "Soulfen", "Greyveil", "Murk-Cradle", "Fadewatch",
            "Hush Stone", "Whispergate", "Old Names", "Dwarvensong",
            "Twin Pillars", "Mirewell", "Ghostbough", "Chitinwarden",
            "Sealed Mouth", "Dim Spire", "Faded Banner", "Hollow Crown",
            "Veiled Path", "Mossglow", "Stillvein", "Thornpath", "Last Echo"
        };

        // Ashlands: blackened, smoldering, hostile.
        private static readonly string[] _ashlands = new[]
        {
            "Emberreach", "Cinderwarden", "Charcrown", "Sootspire",
            "Magma Hold", "Ashfall", "Brimstone Pass", "Pyregate",
            "Forge-Of-Last-Kings", "Ashen Choir", "Soulflame", "Black Glass",
            "Burnt Banner", "Hellsmouth", "Coalspire", "Volcanus",
            "Ember Cradle", "Cinderforge", "Last Smoke", "Asherake",
            "Phoenixfall", "Fyrwarden", "Glowblood", "Dusk Throne",
            "Smolderpath", "Slaglight", "Ironbreath", "Char-Crown"
        };

        // Deep North: bleak, silent, white.
        private static readonly string[] _deepnorth = new[]
        {
            "Whitewatch", "Frostheart", "Silent Cradle", "Glacierfall",
            "Whispersnow", "Pale Throne", "Last Light", "Iceveil",
            "Bonecold", "Northwarden", "Hush Spire", "Silvermoor",
            "Crystal Pass", "Stillpeak", "Frostmaiden", "Witherwhite",
            "Cloudbreath", "Snowsong", "Hibernation", "Blue Throne",
            "Lonelyrise", "Pale Cradle", "Frozen Choir", "Glassfrost",
            "Heart Of Winter", "Snowmaul", "Whitewhisper", "Northstar"
        };

        // Ocean: maritime, drowned-saint, isolated.
        private static readonly string[] _ocean = new[]
        {
            "Tidewatch", "Saltspire", "Drowned Lantern", "Wavecrown",
            "Stormgate", "Krakenrest", "Shoreward", "Salt Stone",
            "Oarsman's Rest", "Foamcrown", "Lighthouse Rock", "Brinewatch",
            "Mermaid's Tear", "Anchorhold", "Driftmark", "Saltrose",
            "Squallspire", "Far-Sail", "Brinekeep", "Stormwarden"
        };
    }
}
