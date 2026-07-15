using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Engine attachment sockets under Hull/Attachments (+Y up, +Z exhaust direction).
    /// VFX parent here with localPosition zero — never derive positions from mesh bounds.
    /// </summary>
    public static class RtgGliderExhaustSockets
    {
        public const string AttachmentsName = "Attachments";
        public const string MainSocketName = "EngineSocket_Main";
        public const string LeftSocketName = "EngineSocket_Left";
        public const string RightSocketName = "EngineSocket_Right";

        // Legacy names still resolve for older prefabs.
        private static readonly string[] MainAliases =
        {
            MainSocketName, "Exhaust_Main", "RTG_Exhaust_Main", "ExhaustMain",
        };
        private static readonly string[] LeftAliases =
        {
            LeftSocketName, "Exhaust_Left", "RTG_Exhaust_Left", "ExhaustLeft",
        };
        private static readonly string[] RightAliases =
        {
            RightSocketName, "Exhaust_Right", "RTG_Exhaust_Right", "ExhaustRight",
        };

        public struct SocketSet
        {
            public Transform Attachments;
            public Transform Main;
            public Transform Left;
            public Transform Right;
            public bool UsedAuthoredSockets;
        }

        /// <summary>
        /// Ensures Attachments + three engine sockets exist under the hull root and applies positions.
        /// Positions are always in Attachments local space (meters).
        /// </summary>
        public static SocketSet Resolve(Transform hullRoot, RtgGliderEngineMounts localPositions)
        {
            var result = new SocketSet();
            if (hullRoot == null)
                return result;

            Transform attachments = EnsureAttachments(hullRoot);
            result.Attachments = attachments;

            Transform existingMain = FindByAliases(attachments, MainAliases);
            Transform existingLeft = FindByAliases(attachments, LeftAliases);
            Transform existingRight = FindByAliases(attachments, RightAliases);
            result.UsedAuthoredSockets = existingMain != null
                && existingLeft != null
                && existingRight != null;

            result.Main = EnsureSocket(attachments, MainSocketName, MainAliases, localPositions.Main);
            result.Left = EnsureSocket(attachments, LeftSocketName, LeftAliases, localPositions.Left);
            result.Right = EnsureSocket(attachments, RightSocketName, RightAliases, localPositions.Right);

            ApplyLocalPositions(result, localPositions);

            Debug.Log(
                result.UsedAuthoredSockets
                    ? "[RTG] Engine sockets resolved under Attachments (authored)."
                    : $"[RTG] Engine sockets created under Attachments — " +
                      $"main={localPositions.Main} left={localPositions.Left} right={localPositions.Right}");
            return result;
        }

        public static Transform EnsureAttachments(Transform hullRoot)
        {
            Transform attachments = hullRoot.Find(AttachmentsName);
            if (attachments == null)
            {
                var go = new GameObject(AttachmentsName);
                go.transform.SetParent(hullRoot, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                attachments = go.transform;
            }

            return attachments;
        }

        public static void ApplyLocalPositions(SocketSet sockets, RtgGliderEngineMounts localPositions)
        {
            if (sockets.Main != null)
                sockets.Main.localPosition = localPositions.Main;
            if (sockets.Left != null)
                sockets.Left.localPosition = localPositions.Left;
            if (sockets.Right != null)
                sockets.Right.localPosition = localPositions.Right;
        }

        public static RtgGliderEngineMounts CaptureLocalPositions(SocketSet sockets)
        {
            return new RtgGliderEngineMounts(
                sockets.Main != null ? sockets.Main.localPosition : Vector3.zero,
                sockets.Left != null ? sockets.Left.localPosition : Vector3.zero,
                sockets.Right != null ? sockets.Right.localPosition : Vector3.zero);
        }

        public static bool TryGetSocket(Transform hullRoot, int engineIndex, out Transform socket)
        {
            socket = null;
            if (hullRoot == null)
                return false;

            Transform attachments = hullRoot.Find(AttachmentsName);
            if (attachments == null)
                return false;

            socket = engineIndex switch
            {
                1 => FindByAliases(attachments, LeftAliases),
                2 => FindByAliases(attachments, RightAliases),
                _ => FindByAliases(attachments, MainAliases),
            };
            return socket != null;
        }

        private static Transform EnsureSocket(
            Transform attachments,
            string canonicalName,
            string[] aliases,
            Vector3 localPosition)
        {
            Transform existing = FindByAliases(attachments, aliases);
            if (existing == null)
            {
                var go = new GameObject(canonicalName);
                go.transform.SetParent(attachments, false);
                existing = go.transform;
            }
            else if (existing.name != canonicalName)
            {
                existing.name = canonicalName;
            }

            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            existing.localPosition = localPosition;
            return existing;
        }

        private static Transform FindByAliases(Transform root, string[] aliases)
        {
            foreach (string alias in aliases)
            {
                Transform found = FindDeepChild(root, alias);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindDeepChild(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
