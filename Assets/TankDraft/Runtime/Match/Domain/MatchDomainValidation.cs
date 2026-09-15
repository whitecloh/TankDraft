using System;
using TankDraft.Contracts.Battle;

namespace TankDraft.Match.Domain
{
    public static class MatchDomainValidation
    {
        public static string Run()
        {
            int checks = 0;
            try
            {
                var rules = new MatchRules(4, 3, 12, 15, 12, 3, 25);
                var units = new[]
                {
                    new MatchUnitDefinition("unit.mines", 1, false),
                    new MatchUnitDefinition("unit.heavy_tank", 1, true),
                    new MatchUnitDefinition("unit.tank_destroyer", 2, true),
                    new MatchUnitDefinition("unit.field_artillery", 3, true)
                };
                var deck = new[]
                {
                    "unit.mines",
                    "unit.heavy_tank",
                    "unit.tank_destroyer",
                    "unit.field_artillery"
                };
                if (!MatchService.IsSupportedDeck(deck, units))
                    throw new InvalidOperationException("supported deck rejected");
                checks++;
                var match = new MatchService(rules, units, deck, deck, "order.reinforce_armor", 7);
                if (match.ChoiceNumber != 1 || match.IsComeback || match.BonusSide != -1 || match.GetArmy(0).Count != 0)
                    throw new InvalidOperationException("first draft state");
                checks++;
                bool prematureRejected = false;
                try
                {
                    match.ResolveBattle(BattleOutcome.Side0Won);
                }
                catch (InvalidOperationException)
                {
                    prematureRejected = true;
                }

                if (!prematureRejected || match.TryChoose(0, match.ChoiceToken, 3, out _))
                    throw new InvalidOperationException("invalid action accepted");
                checks++;
                long stale = match.ChoiceToken;
                ChooseBoth(match, 0);
                if (match.TryChoose(0, stale, 0, out _))
                    throw new InvalidOperationException("stale choice accepted");
                checks++;
                FinishDraft(match);
                if (match.Phase != MatchPhase.Battle || match.CreateScenario().Stacks.Count < 2)
                    throw new InvalidOperationException("initial battle");
                checks++;
                match.ResolveBattle(BattleOutcome.Side1Won);
                match.Continue();
                if (!match.IsComeback || match.BonusSide != 0 || match.ChoiceNumber != 0)
                    throw new InvalidOperationException("comeback state");
                checks++;
                ChooseBonusAndDraft(match);
                match.ResolveBattle(BattleOutcome.Side0Won);
                match.Continue();
                if (!match.IsComeback || match.BonusSide != 1)
                    throw new InvalidOperationException("opposite comeback state");
                checks++;
                ChooseBonusAndDraft(match);
                match.ResolveBattle(BattleOutcome.Side0Won);
                match.Continue();
                ChooseBonusAndDraft(match);
                match.ResolveBattle(BattleOutcome.Side0Won);
                match.Continue();
                ChooseBonusAndDraft(match);
                match.ResolveBattle(BattleOutcome.Side0Won);
                if (match.Phase != MatchPhase.MatchResult || match.Wins(0) != 4)
                    throw new InvalidOperationException("fourth win did not end match");
                checks++;
                bool finalContinueRejected = false;
                try
                {
                    match.Continue();
                }
                catch (InvalidOperationException)
                {
                    finalContinueRejected = true;
                }

                if (!finalContinueRejected || match.GetOffers(0).Count != 0)
                    throw new InvalidOperationException("final match exposed new choices");
                checks++;
                var ordered = new MatchService(rules, units, deck, deck, "order.reinforce_armor", 99);
                ChooseBoth(ordered, FirstMobileOffer(ordered, 0));
                long orderToken = ordered.ChoiceToken;
                if (!ordered.TryUseOrder(0, orderToken, out var orderReason))
                    throw new InvalidOperationException(orderReason);
                if (!ordered.TryChoose(1, orderToken, 0, out var botReason))
                    throw new InvalidOperationException(botReason);
                if (ordered.OrderCharges != 2 || ordered.CanUseOrder(0) || !HasArmorBonus(ordered.CreateScenario(), 0, 25))
                    throw new InvalidOperationException("order charge or current-choice armor");
                checks++;
                FinishDraft(ordered);
                ordered.ResolveBattle(BattleOutcome.Side0Won);
                if (ordered.Phase != MatchPhase.RoundResult)
                    throw new InvalidOperationException("order round result");
                checks++;
                var review = new MatchService(rules, units, deck, deck, "", 2);
                FinishDraft(review);
                review.ResolveBattle(BattleOutcome.ReviewRequired);
                if (review.Phase != MatchPhase.ReviewRequired || review.Wins(0) != 0 || review.Wins(1) != 0)
                    throw new InvalidOperationException("review outcome changed score");
                checks++;
                bool duplicateRejected = false;
                try
                {
                    new MatchService(rules, units, new[] { "unit.mines", "unit.mines", "unit.tank_destroyer", "unit.field_artillery" }, deck, "", 1);
                }
                catch (ArgumentException)
                {
                    duplicateRejected = true;
                }

                if (!duplicateRejected)
                    throw new InvalidOperationException("duplicate deck accepted");
                checks++;
                return "MatchDomainValidation passed " + checks + " checks: first, barrier/stale/invalid, comeback, four-win final, review and duplicate deck.";
            }
            catch (Exception ex)
            {
                return "MatchDomainValidation FAILED after " + checks + " checks: " + ex;
            }
        }

        static void ChooseBoth(MatchService match, int index)
        {
            long token = match.ChoiceToken;
            int before = match.GetArmy(0).Count;
            if (!match.TryChoose(0, token, index, out var first))
                throw new InvalidOperationException(first);
            if (match.GetArmy(0).Count != before)
                throw new InvalidOperationException("pending choice mutated army");
            if (!match.TryChoose(1, token, index, out var second))
                throw new InvalidOperationException(second);
        }

        static int FirstMobileOffer(MatchService match, int side)
        {
            var offers = match.GetOffers(side);
            for (int i = 0; i < offers.Count; i++)
                if (offers[i].UnitId != "unit.mines")
                    return i;
            throw new InvalidOperationException("mobile offer missing");
        }

        static bool HasArmorBonus(BattleScenarioDefinition scenario, int side, int percent)
        {
            for (int i = 0; i < scenario.Stacks.Count; i++)
                if (scenario.Stacks[i].Side == side && scenario.Stacks[i].HpBonusPercent >= percent)
                    return true;
            return false;
        }

        static void FinishDraft(MatchService match)
        {
            while (match.Phase == MatchPhase.Draft)
                ChooseBoth(match, 0);
        }

        static void ChooseBonusAndDraft(MatchService match)
        {
            if (match.IsComeback)
            {
                int side = match.BonusSide;
                long token = match.ChoiceToken;
                if (!match.TryChoose(side, token, 0, out var reason))
                    throw new InvalidOperationException(reason);
            }

            FinishDraft(match);
        }
    }
}
