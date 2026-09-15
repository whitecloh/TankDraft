using System;
using System.Collections.Generic;
using System.IO;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;

namespace TankDraft.Match.Networking
{
    public static class NetCoreValidation
    {
        public static string Run(BattleDefinitions definitions, BattleRules battleRules, MatchRules matchRules, MatchUnitDefinition[] units)
        {
            if (definitions == null || battleRules == null || matchRules == null || units == null || units.Length < 4)
                throw new ArgumentException("fixture");
            string[] deck =
            {
                units[0].Id,
                units[1].Id,
                units[2].Id,
                units[3].Id
            };
            var authority = new NetMatchAuthority(definitions, battleRules, matchRules, units, 10, 20, deck, deck, "order.reinforce_armor", "order.reinforce_armor", 7, "validation.match", "validation.v1");
            try
            {
                NetFrame frame0 = authority.Capture(0, 1, 1.5, false);
                frame0.Events = new[]
                {
                    new BattleEvent(7, 3, BattleEventKind.Impact, 2, 1, new BattleVec(1, 2), 5, .5f, .25f)
                };
                frame0.Army = new[]
                {
                    new ArmyEntry("validation.unit", 1, 2)
                };
                frame0.RandomTieBreak = true;
                frame0.TieBreakSeed = 7;
                byte[] wire = NetCodec.EncodeFrame(frame0);
                NetFrame decoded = NetCodec.DecodeFrame(wire);
                Assert(decoded.StateHash == frame0.StateHash && decoded.Events[0].TimeWithinTick == .25f && decoded.Army.Length == 1 && decoded.Army[0].UpgradeLevel == 2 && decoded.RandomTieBreak && decoded.TieBreakSeed == 7, "codec-roundtrip");
                Assert(decoded.Side == 0 && decoded.Offers.Length == frame0.Offers.Length, "privacy-viewer");
                var choose = new NetCommand
                {
                    MatchId = "validation.match",
                    ContentVersion = "validation.v1",
                    Round = authority.Match.RoundNumber,
                    Token = authority.Match.ChoiceToken,
                    OfferIndex = 0,
                    Sequence = 1,
                    Kind = NetCommandKind.Choose
                };
                Assert(authority.Handle(10, choose).Accepted, "choose");
                Assert(authority.Capture(0, 2, 1.6, false).Offers.Length == 0, "committed-offers-hidden");
                Assert(authority.Handle(10, choose).Duplicate, "duplicate");
                var stale = new NetCommand
                {
                    MatchId = choose.MatchId,
                    ContentVersion = choose.ContentVersion,
                    Round = choose.Round,
                    Token = choose.Token,
                    OfferIndex = 0,
                    Sequence = 0,
                    Kind = NetCommandKind.Choose
                };
                Assert(!authority.Handle(10, stale).Accepted, "stale");
                var wrongVersion = new NetCommand
                {
                    MatchId = choose.MatchId,
                    ContentVersion = "wrong",
                    Round = choose.Round,
                    Token = choose.Token,
                    OfferIndex = 0,
                    Sequence = 1,
                    Kind = NetCommandKind.Choose
                };
                Assert(!authority.Handle(20, wrongVersion).Accepted, "version");
                Assert(!authority.Handle(99, choose).Accepted, "wrong-actor");
                bool hashRejected = false;
                wire[wire.Length - 1] ^= 1;
                try
                {
                    NetCodec.DecodeFrame(wire);
                }
                catch (InvalidDataException)
                {
                    hashRejected = true;
                }

                Assert(hashRejected, "state-hash");
                bool payloadRejected = false;
                try
                {
                    NetCodec.EncodeFrame(new NetFrame { MatchId = "m", ContentVersion = "v", Round = 1, Side = 0, StateHash = "x", Offers = TooManyOffers() });
                }
                catch (InvalidDataException)
                {
                    payloadRejected = true;
                }

                Assert(payloadRejected, "payload");
                bool malformedPrefixRejected = false;
                try
                {
                    NetCodec.DecodeCommand(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });
                }
                catch (InvalidDataException)
                {
                    malformedPrefixRejected = true;
                }

                Assert(malformedPrefixRejected, "malformed-prefix");
                long sequence0 = 1, sequence1 = 2;
                Assert(authority.Handle(20, Command(authority, NetCommandKind.Choose, 0, ++sequence1)).Accepted, "preview-second-commit");
                Assert(authority.Simulation != null && authority.Simulation.Tick == 0 && authority.Match.Phase == MatchPhase.Draft, "preview-tick-zero");
                authority.Advance(5);
                Assert(authority.Simulation.Tick == 0, "advance-draft-does-nothing");
                FinishAuthorityDraft(authority, ref sequence0, ref sequence1);
                Assert(authority.Match.Phase == MatchPhase.Battle, "authority-finish-draft");
                authority.Match.ResolveBattle(BattleOutcome.Side0Won);
                Assert(authority.Handle(10, Command(authority, NetCommandKind.Continue, 0, ++sequence0)).Accepted, "continue-ready0");
                Assert(authority.Handle(20, Command(authority, NetCommandKind.Continue, 0, ++sequence1)).Accepted, "continue-ready1");
                NetFrame nextRound = authority.Capture(0, 3, 1.7, false);
                Assert(authority.Match.RoundNumber == 2 && authority.Simulation != null && authority.Simulation.Tick == 0 && nextRound.EventWatermark == 0 && nextRound.Events.Length == 0, "new-round-preview-watermark-reset");
            }
            finally
            {
                authority.Dispose();
            }

            ValidateTwoSidedOrder(matchRules, units, deck);
            ValidateFourWins(matchRules, units, deck);
            return "PASS NetCoreValidation codec/privacy/duplicate/stale/version/wrongactor/fourwins/twosidedorder/stateshash/payload/malformed-prefix/preview";
        }

        static MatchOffer[] TooManyOffers()
        {
            var value = new MatchOffer[4];
            for (int i = 0; i < value.Length; i++)
                value[i] = new MatchOffer("o" + i, "u", DraftActionKind.Add, 1);
            return value;
        }

        static void ValidateTwoSidedOrder(MatchRules rules, MatchUnitDefinition[] units, string[] deck)
        {
            var match = new MatchService(rules, units, deck, deck, "order.reinforce_armor", 2, "order.reinforce_armor");
            long token = match.ChoiceToken;
            Assert(match.TryChoose(0, token, FirstFighterOffer(match, 0, units), out var choose0), choose0);
            Assert(match.TryChoose(1, token, FirstFighterOffer(match, 1, units), out var choose1), choose1);
            Assert(match.CanUseOrder(0) && match.CanUseOrder(1), "two-sided-order-ready");
            int left0 = match.Charges(0), left1 = match.Charges(1);
            Assert(match.TryUseOrder(0, match.ChoiceToken, out var reason0), reason0);
            Assert(match.TryUseOrder(1, match.ChoiceToken, out var reason1), reason1);
            Assert(match.Charges(0) == left0 - 1 && match.Charges(1) == left1 - 1 && match.OrderCharges == match.Charges(0), "two-sided-order");
        }

        static void FinishAuthorityDraft(NetMatchAuthority authority, ref long sequence0, ref long sequence1)
        {
            int guard = 32;
            while (authority.Match.Phase == MatchPhase.Draft && guard-- > 0)
            {
                if (authority.Match.IsComeback)
                {
                    int side = authority.Match.BonusSide;
                    Assert(authority.Handle(side == 0 ? 10 : 20, Command(authority, NetCommandKind.Choose, 0, side == 0 ? ++sequence0 : ++sequence1)).Accepted, "authority-bonus");
                }
                else
                {
                    Assert(authority.Handle(10, Command(authority, NetCommandKind.Choose, 0, ++sequence0)).Accepted, "authority-choose0");
                    Assert(authority.Handle(20, Command(authority, NetCommandKind.Choose, 0, ++sequence1)).Accepted, "authority-choose1");
                }
            }

            Assert(authority.Match.Phase == MatchPhase.Battle, "authority-draft-guard");
        }

        static NetCommand Command(NetMatchAuthority authority, NetCommandKind kind, int offer, long sequence)
        {
            return new NetCommand
            {
                MatchId = authority.MatchId,
                ContentVersion = authority.ContentVersion,
                Round = authority.Match.RoundNumber,
                Token = authority.Match.ChoiceToken,
                OfferIndex = offer,
                Sequence = sequence,
                Kind = kind
            };
        }

        static void ValidateFourWins(MatchRules rules, MatchUnitDefinition[] units, string[] deck)
        {
            var match = new MatchService(rules, units, deck, deck, "", 3);
            for (int round = 0; round < rules.WinsRequired; round++)
            {
                FinishDraft(match);
                match.ResolveBattle(BattleOutcome.Side0Won);
                if (round + 1 < rules.WinsRequired)
                    match.Continue();
            }

            Assert(match.Phase == MatchPhase.MatchResult && match.Wins(0) == rules.WinsRequired, "four-wins");
        }

        static void FinishDraft(MatchService match)
        {
            int guard = 32;
            while (match.Phase == MatchPhase.Draft && guard-- > 0)
            {
                if (match.IsComeback)
                {
                    int side = match.BonusSide;
                    Assert(match.TryChoose(side, match.ChoiceToken, 0, out var bonusReason), bonusReason);
                }
                else
                    ChooseBoth(match);
            }

            Assert(match.Phase == MatchPhase.Battle, "finish-draft");
        }

        static void ChooseBoth(MatchService match)
        {
            long token = match.ChoiceToken;
            Assert(match.TryChoose(0, token, 0, out var a), a);
            Assert(match.TryChoose(1, token, 0, out var b), b);
        }

        static int FirstFighterOffer(MatchService match, int side, MatchUnitDefinition[] units)
        {
            var offers = match.GetOffers(side);
            for (int offer = 0; offer < offers.Count; offer++)
                for (int unit = 0; unit < units.Length; unit++)
                    if (units[unit].CanFightAlone && offers[offer].UnitId == units[unit].Id)
                        return offer;
            throw new InvalidOperationException("fighter-offer");
        }

        static void Assert(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
        }
    }
}
