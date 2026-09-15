using UnityEngine;

namespace TankDraft.Match.Presentation
{
    public sealed class MatchCardModel
    {
        public string Title { get; set; }
        public string ActionLabel { get; set; }
        public string Description { get; set; }
        public Sprite Icon { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class MatchViewModel
    {
        // Temporary presentation identity. Network offer ids will replace the fallback signature.
        public string OfferPresentationKey { get; set; }
        // Identity and age are presentation-only projections of an authoritative snapshot.
        // They must never be used to advance a round or submit a command.
        public string MatchPresentationId { get; set; }
        public string PhaseName { get; set; }
        public int RoundNumber { get; set; }
        public int OwnWins { get; set; }
        public int OpponentWins { get; set; }
        public int WinsRequired { get; set; }
        public double BattleElapsedSeconds { get; set; }
        public bool PresentationReset { get; set; }
        public bool WaitingForOpponent { get; set; }
        public string WaitingLabel { get; set; }
        public MatchCardModel WaitingCard { get; set; }
        public string Title { get; set; }
        public string Score { get; set; }
        public string Status { get; set; }
        public string Army0 { get; set; }
        public string Army1 { get; set; }
        public string Hint { get; set; }
        public string OrderLabel { get; set; }
        public string NextLabel { get; set; }
        public string MenuLabel { get; set; }
        public bool DraftVisible { get; set; }
        public bool OrderVisible { get; set; }
        public bool OrderEnabled { get; set; }
        public bool MenuVisible { get; set; } = true;
        public bool NextVisible { get; set; }
        public MatchCardModel[] Cards { get; set; }
    }
}
