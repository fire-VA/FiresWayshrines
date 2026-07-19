using UnityEngine;
using UnityEngine.Rendering;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Placement-ghost wireframe for the trigger-box wayshrine (Wayshrine_Trigger). Added at runtime to the
    /// prefab TEMPLATE so both the GHOST and PLACED instances carry it — but it only ever DRAWS on the ghost.
    /// Unlike <see cref="Wayshrine"/> it is NOT ZDO-gated, which is exactly what lets it run on the ghost
    /// (whose ZNetView is force-disabled and has no ZDO).
    ///
    ///   Ghost  → gold wireframe so you can see the volume you are placing (and scale it).
    ///   Placed → nothing is drawn at all. The box is fully invisible; only its BoxCollider remains, which is
    ///            what keeps it hoverable/interactable and removable with the hammer.
    ///
    /// The collider is deliberately never disabled: the hammer's remove ray (m_removeRayMask, which includes
    /// piece_nonsolid) has to hit it, and Player.RemovePiece works on a Piece+ZNetView with no WearNTear.
    ///
    /// On placed instances the component disables itself but still EXISTS — it is the marker the scaling patch
    /// and the admin "Set trigger size" option use to identify a trigger box (GetComponent returns disabled
    /// components).
    /// </summary>
    public class WayshrineTriggerWire : MonoBehaviour
    {
        private static readonly Color WireColor = new Color(1f, 0.78f, 0.30f, 1f);

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }   // no visuals on a headless server

            var nv = GetComponent<ZNetView>();
            bool isGhost = nv == null || nv.GetZDO() == null;
            if (!isGhost) { enabled = false; return; }                  // placed → invisible, draw nothing

            BuildWire();
        }

        private void BuildWire()
        {
            var child = new GameObject("TriggerWire");   // NOT "_GhostOnly" (that name is vanilla's ghost channel)
            child.transform.SetParent(transform, false);

            child.AddComponent<MeshFilter>().sharedMesh = UnitBoxLineMesh();

            var wire = child.AddComponent<MeshRenderer>();
            wire.sharedMaterial = WireMaterial();
            wire.shadowCastingMode = ShadowCastingMode.Off;
            wire.receiveShadows = false;
            wire.lightProbeUsage = LightProbeUsage.Off;
            wire.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---- shared mesh + material ----

        private static Material _mat;
        private static Material WireMaterial()
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _mat = new Material(sh) { color = WireColor, hideFlags = HideFlags.HideAndDontSave };
            return _mat;
        }

        private static Mesh _lineMesh;
        private static Mesh UnitBoxLineMesh()
        {
            if (_lineMesh != null) return _lineMesh;
            var v = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, 0.5f),
            };
            var idx = new[] { 0, 1, 1, 2, 2, 3, 3, 0,  4, 5, 5, 6, 6, 7, 7, 4,  0, 4, 1, 5, 2, 6, 3, 7 };
            _lineMesh = new Mesh { name = "WayshrineTriggerWire", hideFlags = HideFlags.HideAndDontSave };
            _lineMesh.vertices = v;
            _lineMesh.SetIndices(idx, MeshTopology.Lines, 0);
            _lineMesh.RecalculateBounds();
            return _lineMesh;
        }
    }
}
