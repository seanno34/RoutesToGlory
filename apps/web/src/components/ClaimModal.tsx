interface ClaimModalProps {
  settlementName: string;
  onFoundTown: () => void;
  onClaimReward: () => void;
  onCancel: () => void;
}

export function ClaimModal({
  settlementName,
  onFoundTown,
  onClaimReward,
  onCancel,
}: ClaimModalProps) {
  return (
    <div className="claim-modal-backdrop" role="presentation" onClick={onCancel}>
      <div
        className="claim-modal"
        role="dialog"
        aria-labelledby="claim-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="claim-title">{settlementName}</h2>
        <p className="claim-modal-sub">Goodie Hut — choose your reward</p>
        <button type="button" onClick={onFoundTown}>
          Found Town
        </button>
        <button type="button" className="secondary" onClick={onClaimReward}>
          Claim Reward
        </button>
        <button type="button" className="link" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}
