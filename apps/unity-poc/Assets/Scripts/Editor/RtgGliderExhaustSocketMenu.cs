using RoutesToGlory.Game;
using UnityEditor;
using UnityEngine;

namespace RoutesToGlory.Editor
{
    /// <summary>
    /// Adds Attachments/EngineSocket_* transforms per the afterburner architecture guide.
    /// Position sockets manually in Scene view — do not rely on mesh bounds.
    /// </summary>
    public static class RtgGliderExhaustSocketMenu
    {
        private const string MenuPath = "Routes to Glory/Add Engine Sockets to Glider";

        [MenuItem(MenuPath, true)]
        private static bool ValidateAddSockets()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(MenuPath)]
        private static void AddSockets()
        {
            GameObject target = Selection.activeGameObject;
            if (target == null)
            {
                EditorUtility.DisplayDialog(
                    "Routes to Glory",
                    "Select the ship Hull root (or glider prefab root) in the Hierarchy.",
                    "OK");
                return;
            }

            Transform hullRoot = FindHullRoot(target.transform);
            if (hullRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Routes to Glory",
                    "Could not find a Hull/Model hierarchy. Select the player Ship or Hull object.",
                    "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(hullRoot.gameObject, "Add Engine Sockets");

            RtgGliderEngineMounts defaults = RtgGliderEngineMounts.BlockoutDefaults(24f);
            RtgGliderExhaustSockets.SocketSet sockets = RtgGliderExhaustSockets.Resolve(hullRoot, defaults);

            Selection.activeTransform = sockets.Main != null ? sockets.Main : sockets.Attachments;
            EditorUtility.SetDirty(hullRoot.gameObject);

            Debug.Log(
                "[RTG] Engine sockets under Hull/Attachments. " +
                "Position each socket at the nozzle center in Scene view; +Z = exhaust direction.");
        }

        private static Transform FindHullRoot(Transform selection)
        {
            if (selection.name == "Hull" || selection.name == "Ship")
                return selection.name == "Ship" ? selection.Find("Hull") ?? selection : selection;

            Transform hull = selection.Find("Hull");
            if (hull != null)
                return hull;

            if (selection.Find("Model") != null || selection.Find("GliderMesh") != null)
                return selection;

            return selection;
        }
    }
}
