using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TankDraft.Contracts
{
    public sealed class UnitProgressSnapshot
    {
        public UnitProgressSnapshot(string id, int level, int bits, int masteryXp)
        {
            if (!ProgressionSnapshot.IsValidId(id)) throw new ArgumentException("Unit progress id is invalid.", nameof(id));
            if (level < 1 || level > 31) throw new ArgumentOutOfRangeException(nameof(level));
            if (bits < 0) throw new ArgumentOutOfRangeException(nameof(bits));
            if (masteryXp < 0) throw new ArgumentOutOfRangeException(nameof(masteryXp));

            Id = id;
            Level = level;
            Bits = bits;
            MasteryXp = masteryXp;
        }

        public string Id { get; }
        public int Level { get; }
        public int Bits { get; }
        public int MasteryXp { get; }
    }

    // Read-only server projection. Prices, grants, and progression decisions remain authoritative on the server.
    public sealed class ProgressionSnapshot
    {
        public const int MaximumUnits = 256;
        public const int MaximumIdLength = 128;
        private readonly ReadOnlyCollection<UnitProgressSnapshot> units;
        private readonly ReadOnlyCollection<string> claimedMilestones;

        public ProgressionSnapshot(long sequence, UnitProgressSnapshot[] units, int masteryChips, string operationStatus = null, string[] claimedMilestones = null, string rulesVersion = null, string[] resolvedUnitIds = null)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (masteryChips < 0) throw new ArgumentOutOfRangeException(nameof(masteryChips));
            if (operationStatus != null && !IsValidId(operationStatus)) throw new ArgumentException("Operation status is invalid.", nameof(operationStatus));

            Sequence = sequence;
            this.units = new ReadOnlyCollection<UnitProgressSnapshot>(CopyUnits(units));
            MasteryChips = masteryChips;
            OperationStatus = operationStatus;
            RulesVersion = rulesVersion;
            ResolvedUnitIds = new ReadOnlyCollection<string>(CopyIds(resolvedUnitIds));
            this.claimedMilestones = new ReadOnlyCollection<string>(CopyIds(claimedMilestones));
        }

        public long Sequence { get; }
        public IReadOnlyList<UnitProgressSnapshot> Units { get { return units; } }
        public int MasteryChips { get; }
        public string OperationStatus { get; }
        public string RulesVersion { get; }
        public IReadOnlyList<string> ResolvedUnitIds { get; }
        public IReadOnlyList<string> ClaimedMilestones { get { return claimedMilestones; } }

        public static bool IsValidId(string value)
        {
            if (value == null || value.Length == 0 || value.Length > MaximumIdLength) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-' && c != ':') return false;
            return true;
        }

        private static UnitProgressSnapshot[] CopyUnits(UnitProgressSnapshot[] source)
        {
            if (source == null || source.Length > MaximumUnits) throw new ArgumentException("Unit progress collection is invalid.", nameof(source));
            UnitProgressSnapshot[] copy = new UnitProgressSnapshot[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                UnitProgressSnapshot unit = source[index] ?? throw new ArgumentException("Unit progress cannot contain null.", nameof(source));
                for (int earlier = 0; earlier < index; earlier++)
                    if (copy[earlier].Id == unit.Id) throw new ArgumentException("Unit progress cannot contain duplicate ids.", nameof(source));
                copy[index] = unit;
            }
            return copy;
        }

        private static string[] CopyIds(string[] source)
        {
            if (source == null) return Array.Empty<string>();
            if (source.Length > MaximumUnits) throw new ArgumentException("Claimed milestone collection is invalid.", nameof(source));
            string[] copy = new string[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                string id = source[index];
                if (!IsValidId(id)) throw new ArgumentException("Claimed milestone id is invalid.", nameof(source));
                for (int earlier = 0; earlier < index; earlier++)
                    if (copy[earlier] == id) throw new ArgumentException("Claimed milestones cannot contain duplicate ids.", nameof(source));
                copy[index] = id;
            }
            return copy;
        }
    }
}
