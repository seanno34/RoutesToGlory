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
        public string ownerEmpireId;

        public bool IsConnected { get; private set; }

        /// <summary>
        /// When true, taps must not open the goodie choice modal again for this
        /// GameObject (set as soon as the modal opens or a choice is submitted).
        /// </summary>
        public bool GoodieInteractionBlocked { get; private set; }

        public void SetConnected(bool connected) => IsConnected = connected;

        public void Configure(
            Kind markerKind,
            string id,
            string name,
            string label,
            double latitude,
            double longitude,
            string ownerId = null)
        {
            kind = markerKind;
            targetId = id;
            displayName = name;
            subLabel = label;
            lat = latitude;
            lng = longitude;
            ownerEmpireId = ownerId ?? "";
        }

        /// <summary>
        /// After a successful goodie claim, clear hut state immediately so the choice
        /// modal cannot reopen before / during map reload.
        /// </summary>
        public void MarkGoodieClaimed(string newTier = "settlement")
        {
            if (!string.IsNullOrEmpty(newTier))
                subLabel = newTier;
            else if (subLabel == "goodie_hut")
                subLabel = "settlement";
            IsConnected = true;
            BlockGoodieInteraction();
        }

        /// <summary>
        /// Disable further goodie taps on this marker instance (colliders + flag).
        /// Does not by itself change <see cref="IsUnclaimedGoodieHut"/> until
        /// <see cref="MarkGoodieClaimed"/> / session Remember runs.
        /// </summary>
        public void BlockGoodieInteraction()
        {
            GoodieInteractionBlocked = true;
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        /// <summary>Re-enable taps after Cancel, only if the hut was never claimed.</summary>
        public void UnblockGoodieInteractionIfUnclaimed()
        {
            if (RtgClaimedGoodieHuts.Contains(targetId) || IsConnected)
                return;
            GoodieInteractionBlocked = false;
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
                col.enabled = true;
        }

        public string KindApiValue => kind == Kind.Settlement ? "settlement" : "resource";

        /// <summary>True when this beacon still offers the one-time goodie choice.</summary>
        public bool IsUnclaimedGoodieHut =>
            kind == Kind.Settlement
            && subLabel == "goodie_hut"
            && string.IsNullOrEmpty(ownerEmpireId)
            && !IsConnected
            && !RtgClaimedGoodieHuts.Contains(targetId);

        public bool IsGoodieHut => kind == Kind.Settlement && subLabel == "goodie_hut";
    }
}
