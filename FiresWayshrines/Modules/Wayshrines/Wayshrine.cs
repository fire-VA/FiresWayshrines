using FiresWayshrines;
using FiresCore.Logging;
using System;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// MonoBehaviour attached to the wayshrine build piece prefab. Implements
    /// <see cref="Hoverable"/> and <see cref="Interactable"/> so vanilla hover
    /// and use logic picks it up.
    ///
    /// Naming
    /// ------
    /// Each placed shrine generates a unique name from
    /// <see cref="WayshrineNamePool"/> on first spawn (seeded from the ZDO
    /// id so reloads/other clients see the same value) and stores it via
    /// Valheim's built-in field-override system: ZDO keys
    /// <c>HasFields</c> + <c>HasFieldsPiece</c> + <c>Piece.m_name</c>.
    /// <see cref="ZNetView.LoadFields"/> applies these to the live
    /// <see cref="Piece.m_name"/> automatically at instantiation, so we
    /// just read <c>_piece.m_name</c> as the source of truth.
    ///
    /// Admin rename happens via alt+RMB on the wayshrine map pin (see
    /// <c>Patches/MinimapClickPatch.cs</c>) which routes through
    /// <see cref="TextReceiver"/> on this MonoBehaviour.
    /// </summary>
    public class Wayshrine : MonoBehaviour, Hoverable, Interactable, TextReceiver, IRemoved
    {
        // Legacy ZDO key from older builds. We migrate any value found here
        // to the field-override system on first load and ignore it after.
        private const string LEGACY_ZDO_KEY_NAME = "VAWayshrine_displayName";

        [Tooltip("Override display name. The ZDO field-override (Piece.m_name) beats this. If both empty, falls back to the prefab Piece.m_name then GameObject name.")]
        public string m_displayName;

        private Piece _piece;
        private ZNetView _nview;
        private float _proximityTimer;
        private const float ProximityCheckInterval = 1f;

        public string ShrineId { get; private set; }

        public string DisplayName
        {
            get
            {
                string raw = (_piece != null && !string.IsNullOrEmpty(_piece.m_name)) ? _piece.m_name : null;
                if (string.IsNullOrEmpty(raw)) raw = m_displayName;
                if (string.IsNullOrEmpty(raw)) raw = gameObject.name;
                return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
            }
        }

        private void Awake()
        {
            _piece = GetComponent<Piece>();
            _nview = GetComponent<ZNetView>();

            if (_nview != null && _nview.GetZDO() == null)
            {
                this.enabled = false;
                return;
            }

            // ShrineId must be STABLE across renames. The piece's m_name
            // changes when an admin renames the shrine (via the field-override
            // system), so we key off the prefab name instead — that's
            // identical to the prefabName the WayshrineWorldRegistry uses
            // when scanning ZDOMan, so live + ZDO entries collide on the same
            // id. Utils.GetPrefabName strips the "(Clone)" suffix from the
            // runtime instance name.
            string prefabName = Utils.GetPrefabName(gameObject);
            if (string.IsNullOrEmpty(prefabName)) prefabName = gameObject.name;
            ShrineId = WayshrineDiscovery.MakeShrineId(prefabName, transform.position);

            EnsureNameAssigned();
            WayshrineRegistry.Register(this);
            WayshrineWorldRegistry.Invalidate();

            // Drop a map pin for this shrine immediately. If the local player
            // has already discovered it, drop a labeled pin; otherwise (and
            // only when the config is enabled) drop a locked "???" pin so
            // they can see something is here without revealing the name.
            WayshrineMapPins.AddPinForShrineRespectingDiscovery(this);

            FiresLogger.LogInfo($"[Wayshrine] Registered shrine '{DisplayName}' id='{ShrineId}' pos={transform.position} liveCount={WayshrineRegistry.All.Count}");
        }

        private void OnDestroy()
        {
            WayshrineRegistry.Unregister(this);
            WayshrineWorldRegistry.Invalidate();
        }

        private void Update()
        {
            _proximityTimer -= Time.deltaTime;
            if (_proximityTimer > 0f) return;
            _proximityTimer = ProximityCheckInterval;

            var player = Player.m_localPlayer;
            if (player == null) return;
            if (player.IsDead() || player.InCutscene()) return;

            float range = WayshrineConfig.DiscoveryRange?.Value ?? 20f;
            Vector3 d = transform.position - player.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > range * range) return;

            if (WayshrineDiscovery.MarkDiscovered(player, ShrineId))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Discovered: {DisplayName}");
                WayshrineMapPins.AddPinForShrine(this);
                FiresLogger.LogInfo($"[Wayshrine] {player.GetPlayerName()} discovered '{DisplayName}' (id={ShrineId})");
            }
        }

        // ---------------- Naming ----------------

        /// <summary>
        /// On first spawn (no ZDO override yet), roll a deterministic name
        /// based on the ZDO id and stash it via the
        /// <c>HasFields</c>/<c>Piece.m_name</c> field-override system.
        /// <see cref="ZNetView.LoadFields"/> picks the override up on every
        /// subsequent load and writes it back into
        /// <see cref="Piece.m_name"/>, so reading <c>_piece.m_name</c> is
        /// always the source of truth from here on.
        /// </summary>
        private void EnsureNameAssigned()
        {
            if (_nview == null || _nview.GetZDO() == null || _piece == null) return;
            var zdo = _nview.GetZDO();

            // 1. ZDO-stored override already in place — LoadFields has already
            //    applied it to _piece.m_name; nothing to do.
            string current = zdo.GetString("Piece.m_name");
            if (!string.IsNullOrEmpty(current))
            {
                _piece.m_name = current; // belt-and-braces in case LoadFields ordering varies
                return;
            }

            // 2. Migrate from old custom key if present (only the owner writes
            //    the field override; other clients pick it up on next load).
            string legacy = zdo.GetString(LEGACY_ZDO_KEY_NAME);
            if (!string.IsNullOrEmpty(legacy))
            {
                _piece.m_name = legacy;
                if (_nview.IsOwner())
                {
                    WriteFieldOverride(zdo, legacy);
                    FiresLogger.LogInfo($"[Wayshrine] Migrated legacy name '{legacy}' to Piece.m_name field override (id={ShrineId})");
                }
                return;
            }

            // 3. Fresh shrine — owner rolls a deterministic name.
            if (!_nview.IsOwner()) return;

            var zdoId = zdo.m_uid;
            int seed = unchecked((int)(zdoId.UserID ^ zdoId.ID));
            var rng = new System.Random(seed);
            var biome = WayshrineNamePool.InferBiomeFromName(
                !string.IsNullOrEmpty(_piece.m_name) ? _piece.m_name : gameObject.name);
            try
            {
                var actual = WorldGenerator.instance?.GetBiome(transform.position) ?? Heightmap.Biome.None;
                if (actual != Heightmap.Biome.None) biome = actual;
            }
            catch { }

            string rolled = WayshrineNamePool.Roll(biome, rng);
            WriteFieldOverride(zdo, rolled);
            _piece.m_name = rolled;
            FiresLogger.LogInfo($"[Wayshrine] Rolled name '{rolled}' for shrine at {transform.position} (biome={biome})");
        }

        private static void WriteFieldOverride(ZDO zdo, string newName)
        {
            zdo.Set("HasFields", true);
            zdo.Set("HasFieldsPiece", true);
            zdo.Set("Piece.m_name", newName);
        }

        /// <summary>
        /// Admin rename. Claims ownership, writes the field override (which
        /// syncs to other clients via ZDO; their <see cref="Piece.m_name"/>
        /// updates the next time their <see cref="ZNetView.LoadFields"/>
        /// runs — i.e. on zone reload). Updates the local piece + map pin
        /// immediately so the rename is visible without a relog locally.
        /// </summary>
        public void RenameByAdmin(string newName)
        {
            if (_nview == null || _nview.GetZDO() == null || _piece == null) return;
            if (string.IsNullOrWhiteSpace(newName)) return;
            string trimmed = newName.Trim();
            if (trimmed.Length > 40) trimmed = trimmed.Substring(0, 40);

            _nview.ClaimOwnership();
            WriteFieldOverride(_nview.GetZDO(), trimmed);
            _piece.m_name = trimmed;

            WayshrineMapPins.RemovePinForShrine(ShrineId);
            WayshrineMapPins.AddPinForShrine(this);

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Wayshrine renamed: {trimmed}");
            FiresLogger.LogInfo($"[Wayshrine] Admin renamed shrine {ShrineId} -> '{trimmed}'");
        }

        // ---------------- Hoverable ----------------

        public string GetHoverText()
        {
            string name = DisplayName;
            string useKey = "$KEY_Use";
            if (Localization.instance != null) useKey = Localization.instance.Localize(useKey);

            string line = $"<color=#FFD966>{name}</color>\n[<color=yellow><b>{useKey}</b></color>] Activate Wayshrine";
            if (IsLocalPlayerAdmin())
                line += "\n<size=80%><color=#888>Admin: alt + right-click pin on map to rename</color></size>";
            return line;
        }

        public string GetHoverName() => DisplayName;

        // ---------------- Interactable ----------------

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user as Player != Player.m_localPlayer) return false;

            WayshrineDiscovery.MarkDiscovered(Player.m_localPlayer, ShrineId);
            WayshrineMapPins.AddPinForShrine(this);
            WayshrineTeleportFlow.BeginFromShrine(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ---------------- IRemoved ----------------

        public void OnRemoved()
        {
            CleanupOnDestruction("hammer-remove");
        }

        internal void CleanupOnDestruction(string source)
        {
            try
            {
                if (string.IsNullOrEmpty(ShrineId)) return;
                WayshrineMapPins.RemovePinForShrine(ShrineId);
                if (Player.m_localPlayer != null
                    && WayshrineDiscovery.RemoveDiscovered(Player.m_localPlayer, ShrineId))
                {
                    FiresLogger.LogInfo($"[Wayshrine] '{DisplayName}' destroyed via {source} — removed pin and discovery entry (id={ShrineId})");
                }
                if (WayshrineTeleportFlow.IsArmed && WayshrineTeleportFlow.ArmedSource == this)
                    WayshrineTeleportFlow.Disarm();
                // Invalidate world registry so the next teleport / scroll lookup
                // doesn't see a freshly-destroyed shrine in its cache.
                WayshrineWorldRegistry.Invalidate();
            }
            catch (Exception ex)
            {
                FiresLogger.LogWarning($"[Wayshrine] CleanupOnDestruction failed: {ex.Message}");
            }
        }

        // ---------------- TextReceiver (admin rename callback) ----------------

        public string GetText() => DisplayName;

        public void SetText(string text)
        {
            if (!IsLocalPlayerAdmin()) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            try { RenameByAdmin(text); }
            catch (Exception ex) { FiresLogger.LogWarning($"[Wayshrine] Rename failed: {ex.Message}"); }
        }

        // ---------------- Admin check ----------------

        /// <summary>
        /// Local-player admin check that works on dedicated servers.
        ///
        /// Vanilla <see cref="ZNet.LocalPlayerIsAdminOrHost"/> reads the
        /// server-only <c>m_adminList</c>. On a dedicated client that list
        /// is empty even when the player IS a real admin, so the vanilla
        /// check returns false and every admin-only branch (the hover hint
        /// here, the alt+RMB rename in <c>MinimapClickPatch</c>, the SetText
        /// guard above) silently no-ops.
        ///
        /// <see cref="ConfigSync.IsAdmin"/> covers both topologies: on the
        /// host it's true via <c>isSourceOfTruth</c>; on a dedicated client
        /// it flips to true once <see cref="AdminSyncing"/> has pushed the
        /// admin-status RPC and set <c>lockExempt</c>. Same pattern that
        /// <c>EnvironmentBoxController.IsLocalPlayerAdmin</c> and
        /// <c>NpcController</c> already use for the same reason.
        /// </summary>
        internal static bool IsLocalPlayerAdmin()
        {
            if (ZNet.instance?.IsServer() == true) return true;
            if (FiresWayshrinesPlugin.Instance?.configSync?.IsAdmin == true) return true;
            return false;
        }
    }
}
