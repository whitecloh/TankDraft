using System.Collections.Generic;

namespace SLVR.UIMotion
{
    public enum UiAttentionRequestResult
    {
        Rejected = 0,
        Started = 1,
        Queued = 2,
        Merged = 3,
    }

    /// <summary>Coordinates one active attention effect per authored scope.</summary>
    internal static class UiAttentionRuntime
    {
        private struct Request
        {
            public UiAttentionPulse Owner;
            public int SemanticKey;
            public UiAttentionEffect Effect;
        }

        private sealed class ScopeState
        {
            public UiAttentionPulse ActiveOwner;
            public int ActiveSemanticKey;
            public readonly List<Request> Queue = new List<Request>(4);
        }

        private static readonly Dictionary<int, ScopeState> Scopes = new Dictionary<int, ScopeState>(8);

        public static UiAttentionRequestResult Submit(
            UiAttentionPulse owner,
            int scopeId,
            int semanticKey,
            UiAttentionEffect effect)
        {
            if (owner == null || !owner.isActiveAndEnabled) return UiAttentionRequestResult.Rejected;
            if (!Scopes.TryGetValue(scopeId, out ScopeState state))
            {
                state = new ScopeState();
                Scopes.Add(scopeId, state);
            }

            if (state.ActiveOwner == null)
            {
                state.ActiveOwner = owner;
                state.ActiveSemanticKey = semanticKey;
                owner.BeginCoordinatedEffect(effect, semanticKey);
                return UiAttentionRequestResult.Started;
            }

            if (ReferenceEquals(state.ActiveOwner, owner) && state.ActiveSemanticKey == semanticKey)
            {
                owner.MergeCoordinatedEffect(effect, semanticKey);
                return UiAttentionRequestResult.Merged;
            }

            for (int i = 0; i < state.Queue.Count; i++)
            {
                Request queued = state.Queue[i];
                if (!ReferenceEquals(queued.Owner, owner) || queued.SemanticKey != semanticKey) continue;
                if (GetPriority(effect) > GetPriority(queued.Effect))
                {
                    queued.Effect = effect;
                    state.Queue[i] = queued;
                }

                owner.NotifyMergedWhileQueued();
                return UiAttentionRequestResult.Merged;
            }

            state.Queue.Add(new Request
            {
                Owner = owner,
                SemanticKey = semanticKey,
                Effect = effect,
            });
            return UiAttentionRequestResult.Queued;
        }

        public static void Complete(UiAttentionPulse owner, int scopeId, int semanticKey)
        {
            if (!Scopes.TryGetValue(scopeId, out ScopeState state)) return;
            if (!ReferenceEquals(state.ActiveOwner, owner) || state.ActiveSemanticKey != semanticKey) return;
            state.ActiveOwner = null;
            state.ActiveSemanticKey = 0;
            StartNextOrRemove(scopeId, state);
        }

        public static void Cancel(UiAttentionPulse owner, int scopeId)
        {
            if (!Scopes.TryGetValue(scopeId, out ScopeState state)) return;
            for (int i = state.Queue.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(state.Queue[i].Owner, owner)) state.Queue.RemoveAt(i);
            }

            if (ReferenceEquals(state.ActiveOwner, owner))
            {
                state.ActiveOwner = null;
                state.ActiveSemanticKey = 0;
            }

            if (state.ActiveOwner != null)
            {
                return;
            }

            StartNextOrRemove(scopeId, state);
        }

        public static int GetPendingCount(int scopeId)
        {
            return Scopes.TryGetValue(scopeId, out ScopeState state) ? state.Queue.Count : 0;
        }

        private static void StartNextOrRemove(int scopeId, ScopeState state)
        {
            while (state.Queue.Count > 0)
            {
                Request next = state.Queue[0];
                state.Queue.RemoveAt(0);
                if (next.Owner == null || !next.Owner.isActiveAndEnabled) continue;
                state.ActiveOwner = next.Owner;
                state.ActiveSemanticKey = next.SemanticKey;
                next.Owner.BeginCoordinatedEffect(next.Effect, next.SemanticKey);
                return;
            }

            Scopes.Remove(scopeId);
        }

        private static int GetPriority(UiAttentionEffect effect)
        {
            switch (effect)
            {
                case UiAttentionEffect.ErrorShake: return 5;
                case UiAttentionEffect.HighlightRing: return 4;
                case UiAttentionEffect.Bounce: return 3;
                case UiAttentionEffect.Pulse: return 2;
                case UiAttentionEffect.Nudge: return 1;
                default: return 0;
            }
        }
    }
}
