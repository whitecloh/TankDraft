using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TankDraft.Contracts
{
    public enum ContentKind
    {
        Unit,
        Order
    }

    public enum FormationRow
    {
        Barrier,
        Tank,
        TankDestroyer,
        Artillery
    }

    public sealed class ContentDefinition
    {
        public ContentDefinition(string id, ContentKind kind, FormationRow row, int requiredArena)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Content id is required.", nameof(id));
            if (!Enum.IsDefined(typeof(ContentKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(FormationRow), row)) throw new ArgumentOutOfRangeException(nameof(row));
            if (requiredArena < 1) throw new ArgumentOutOfRangeException(nameof(requiredArena));
            Id = id;
            Kind = kind;
            Row = row;
            RequiredArena = requiredArena;
        }

        public string Id { get; }
        public ContentKind Kind { get; }
        public FormationRow Row { get; }
        public int RequiredArena { get; }
    }

    public sealed class ArmyRules
    {
        private readonly ReadOnlyCollection<int> orderUnlockLevels;

        public ArmyRules(int unitSlots, int[] orderUnlockLevels)
        {
            if (unitSlots != 4) throw new ArgumentOutOfRangeException(nameof(unitSlots));
            if (orderUnlockLevels == null) throw new ArgumentNullException(nameof(orderUnlockLevels));
            if (orderUnlockLevels.Length != 3) throw new ArgumentException("Exactly three order unlock levels are required.", nameof(orderUnlockLevels));

            int previous = 0;
            for (int index = 0; index < orderUnlockLevels.Length; index++)
            {
                int level = orderUnlockLevels[index];
                if (level < 1 || level < previous) throw new ArgumentException("Order unlock levels must be sorted and at least one.", nameof(orderUnlockLevels));
                previous = level;
            }

            int[] copy = (int[])orderUnlockLevels.Clone();
            UnitSlots = unitSlots;
            OrderSlots = copy.Length;
            this.orderUnlockLevels = new ReadOnlyCollection<int>(copy);
        }

        public int UnitSlots { get; private set; }
        public int OrderSlots { get; private set; }
        public IReadOnlyList<int> OrderUnlockLevels { get { return orderUnlockLevels; } }
    }

    public sealed class MetaDefinitions
    {
        private readonly ReadOnlyCollection<ContentDefinition> entries;
        private readonly Dictionary<string, ContentDefinition> byId;

        public MetaDefinitions(ContentDefinition[] entries, ArmyRules rules)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            ContentDefinition[] copy = new ContentDefinition[entries.Length];
            byId = new Dictionary<string, ContentDefinition>(entries.Length, StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                ContentDefinition entry = entries[index];
                if (entry == null) throw new ArgumentException("Content entries cannot contain null.", nameof(entries));
                if (byId.ContainsKey(entry.Id)) throw new ArgumentException("Content ids must be unique.", nameof(entries));
                byId.Add(entry.Id, entry);
                copy[index] = entry;
            }

            this.entries = new ReadOnlyCollection<ContentDefinition>(copy);
            Rules = rules;
        }

        public IReadOnlyList<ContentDefinition> Entries { get { return entries; } }
        public ArmyRules Rules { get; }

        public ContentDefinition Get(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            ContentDefinition definition;
            if (!byId.TryGetValue(id, out definition)) throw new KeyNotFoundException("Unknown content id: " + id);
            return definition;
        }

        public bool TryGet(string id, out ContentDefinition definition)
        {
            if (id == null)
            {
                definition = null;
                return false;
            }

            return byId.TryGetValue(id, out definition);
        }
    }
}
