using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Rotates this object every frame so it faces the main camera — used for the
    /// floating text labels above Echo Site / resource beacons so they stay readable
    /// from any angle. Runs in edit mode too so labels orient in the Scene view.
    /// </summary>
    [ExecuteAlways]
    public class RtgBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;

            // Face away from the camera so the text (which reads along +Z) is legible.
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}
