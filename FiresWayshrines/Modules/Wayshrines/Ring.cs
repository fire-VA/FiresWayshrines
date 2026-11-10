using System;
using System.Reflection;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    // 1.2.9 halo ring local up (the same helper as FiresRPGClasses 0.1.196 Combat\Ring.cs). Core 0.2.815+ FiresCore.World.GravityUp,
    // read by reflection so an older Core still loads. Off the ring (or no Core API) every method gives exactly the old world-Y
    // answer. Never throws.
    internal static class Ring
    {
        private static bool s_resolved, s_logged;
        private static MethodInfo s_inField, s_at;

        private static void Resolve()
        {
            if (s_resolved) return;
            s_resolved = true;
            try
            {
                var t = Type.GetType("FiresCore.World.GravityUp, FiresUnifiedCore", false);
                if (t == null) return;
                s_inField = t.GetMethod("InField", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
                s_at = t.GetMethod("At", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
                var active = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                if (s_inField == null || s_inField.ReturnType != typeof(bool) || s_at == null || s_at.ReturnType != typeof(Vector3) || active == null) { s_inField = null; s_at = null; return; }
                // bound once as delegates: no reflection Invoke / object[] per call
                s_active = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), active);
                s_inFieldFn = (Func<Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, bool>), s_inField);
                s_atFn = (Func<Vector3, Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3>), s_at);
            }
            catch { s_inField = null; s_at = null; s_active = null; s_inFieldFn = null; s_atFn = null; }
        }

        // Fire 10-26 ("none of our ringworld logic ... ever runs on any world that is not a ring world"): Core's GravityUp.Active (the ONE
        // ring authority, a cached bool) is asked first, so off the ring every call is one cheap bool check: no position test, no allocation.
        private static Func<bool> s_active;
        private static Func<Vector3, bool> s_inFieldFn;
        private static Func<Vector3, Vector3> s_atFn;

        public static bool InField(Vector3 pos)
        {
            Resolve();
            if (s_active == null) return false;
            try { return s_active() && s_inFieldFn(pos); }
            catch { return false; }
        }

        public static Vector3 Up(Vector3 pos)
        {
            if (!InField(pos)) return Vector3.up;
            Vector3 up;
            try { up = s_atFn(pos); }
            catch { return Vector3.up; }
            if (up.sqrMagnitude < 0.0001f) return Vector3.up;
            up = up.normalized;
            if (!s_logged && up != Vector3.up)
            {
                s_logged = true;
                FiresWayshrines.FiresWayshrinesPlugin.Log?.LogDebug($"[Ring] halo ring at {pos:F1}: up {up:F2}, wayshrine landing / facing / lifts use the local up");
            }
            return up;
        }

        // off the ring: v.y = 0; on the ring: the tangent-plane part
        public static Vector3 Flat(Vector3 v, Vector3 pos)
        {
            if (!InField(pos)) { v.y = 0f; return v; }
            return Vector3.ProjectOnPlane(v, Up(pos));
        }

        // "XZ distance" for ranges: off the ring v.y = 0; on the ring the full 3D vector (a shrine up the band is not 0 m away)
        public static Vector3 Reach(Vector3 v, Vector3 pos)
        {
            if (!InField(pos)) { v.y = 0f; return v; }
            return v;
        }

        // world-frame vector (x, y, z) in the local frame at pos: unchanged off the ring
        public static Vector3 Local(Vector3 v, Vector3 pos)
        {
            if (!InField(pos)) return v;
            return Quaternion.FromToRotation(Vector3.up, Up(pos)) * v;
        }
    }
}
