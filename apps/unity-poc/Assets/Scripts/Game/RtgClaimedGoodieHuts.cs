using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Session-scoped set of goodie hut settlement IDs that have already been
    /// claimed (or were known claimed). Blocks the choice modal immediately and
    /// survives marker respawn / map reload within this Play session.
    /// LiveApi still persists via the server; this set is the client gate and a
    /// SampleFile / offline fallback.
    ///
    /// Also tracks the corridor tap-test pin: that slot is single-use so a map
    /// reload cannot place a different unclaimed hut on the same interactive spot.
    /// </summary>
    public static class RtgClaimedGoodieHuts
    {
        private static readonly HashSet<string> Ids = new();
        private static string _corridorPinSettlementId;
        private static bool _corridorPinRetired;

        public static bool Contains(string settlementId) =>
            !string.IsNullOrEmpty(settlementId) && Ids.Contains(settlementId);

        public static void Remember(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return;
            Ids.Add(settlementId);
            if (!_corridorPinRetired
                && !string.IsNullOrEmpty(_corridorPinSettlementId)
                && settlementId == _corridorPinSettlementId)
            {
                _corridorPinRetired = true;
            }
        }

        public static void Forget(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return;
            Ids.Remove(settlementId);
        }

        /// <summary>Sticky corridor pin for this Play session (null when none / retired).</summary>
        public static string CorridorPinSettlementId =>
            _corridorPinRetired ? null : _corridorPinSettlementId;

        public static bool CorridorPinRetired => _corridorPinRetired;

        /// <summary>
        /// Bind the tap-test corridor slot to one settlement. Once that hut is
        /// claimed, <see cref="CorridorPinRetired"/> stays true until Clear.
        /// </summary>
        public static void BindCorridorPin(string settlementId)
        {
            if (_corridorPinRetired) return;
            if (string.IsNullOrEmpty(settlementId)) return;
            if (string.IsNullOrEmpty(_corridorPinSettlementId))
                _corridorPinSettlementId = settlementId;
        }

        /// <summary>Force-retire the corridor slot (e.g. right after a claim choice).</summary>
        public static void RetireCorridorPin(string claimedSettlementId = null)
        {
            if (_corridorPinRetired) return;
            if (string.IsNullOrEmpty(_corridorPinSettlementId))
                return;
            if (string.IsNullOrEmpty(claimedSettlementId)
                || claimedSettlementId == _corridorPinSettlementId)
            {
                _corridorPinRetired = true;
            }
        }

        public static void Clear()
        {
            Ids.Clear();
            _corridorPinSettlementId = null;
            _corridorPinRetired = false;
        }
    }
}
