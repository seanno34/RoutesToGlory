using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Identifies a spawned map beacon (Echo Site or resource node) for tap-to-connect.
    /// Attached to each marker root under RTG Echo Sites → Markers.
    /// </summary>
    public class RtgMapMarker : MonoBehaviour
    {
        public enum Kind { Settlement, Resource }

        public Kind kind;
        public string targetId;
        public string displayName;
        public string subLabel; // tier or richness
        public double lat;
        public double lng;

        public bool IsConnected { get; private set; }

        public void SetConnected(bool connected) => IsConnected = connected;

        public void Configure(Kind markerKind, string id, string name, string label, double latitude, double longitude)
        {
            kind = markerKind;
            targetId = id;
            displayName = name;
            subLabel = label;
            lat = latitude;
            lng = longitude;
        }

        public string KindApiValue => kind == Kind.Settlement ? "settlement" : "resource";
    }
}
