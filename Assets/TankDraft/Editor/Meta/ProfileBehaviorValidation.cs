using System;
using System.IO;
using TankDraft.Application;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.UI
{
    public static class ProfileBehaviorValidation
    {
        private const string CatalogPath = "Assets/TankDraft/Configs/Meta/Catalogs/MetaCatalog.asset";
        private const string MediumTankId = "unit.medium_tank";
        private const string RepairOrderId = "order.repair_team";
        private const string SmokeOrderId = "order.smoke_screen";
        private static int passed;

        public static string Run()
        {
            passed = 0;
            MetaCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<MetaCatalogAsset>(CatalogPath);
            Check(catalog != null, "Meta catalog fixture is missing.");
            string sourceHashBefore = Hash128.Compute(EditorJsonUtility.ToJson(catalog)).ToString();
            MetaDefinitions definitions = catalog.CreateDefinitions();
            ProfileSnapshot seed = catalog.CreateInitialProfile();
            Check(Contains(seed.OwnedIds, MediumTankId), "Seed must own unit.medium_tank.");
            Check(Contains(seed.OwnedIds, RepairOrderId), "Seed must own order.repair_team.");
            Check(Contains(seed.OwnedIds, SmokeOrderId), "Seed must own order.smoke_screen.");

            ValidateSnapshotCopies(seed);
            ValidateInitialization(definitions, seed);
            ValidateProfileRejections(definitions, seed);
            ProfileSnapshot levelThirtyFive = CreateLevelThirtyFiveFixture(definitions, seed);
            ValidateEquipBehavior(definitions, levelThirtyFive);
            ValidateOrderLocks(definitions, seed, levelThirtyFive);
            ValidateDiskRepository(definitions, levelThirtyFive);

            string sourceHashAfter = Hash128.Compute(EditorJsonUtility.ToJson(catalog)).ToString();
            Check(sourceHashBefore == sourceHashAfter, "Profile behavior validation mutated the catalog asset.");
            return "PASS " + passed;
        }

        private static void ValidateSnapshotCopies(ProfileSnapshot seed)
        {
            string[] owned = Copy(seed.OwnedIds);
            string[] units = Copy(seed.UnitIds);
            string[] orders = Copy(seed.OrderIds);
            ProfileSnapshot copy = new ProfileSnapshot(seed.PlayerName, seed.CommanderLevel, seed.ArenaLevel, seed.ArenaProgress, seed.Energy, seed.Gems, seed.Coins, seed.Mastery, owned, units, orders);
            string ownedBefore = copy.OwnedIds[0];
            string unitBefore = copy.UnitIds[0];
            string orderBefore = copy.OrderIds[0];
            owned[0] = "changed";
            units[0] = "changed";
            orders[0] = "changed";
            Check(copy.OwnedIds[0] == ownedBefore && copy.UnitIds[0] == unitBefore && copy.OrderIds[0] == orderBefore, "Profile constructor must clone arrays.");

            string[] replacementUnits = Copy(copy.UnitIds);
            string[] replacementOrders = Copy(copy.OrderIds);
            ProfileSnapshot replaced = copy.WithLoadout(replacementUnits, replacementOrders);
            replacementUnits[0] = unitBefore;
            replacementOrders[0] = orderBefore;
            Check(!ReferenceEquals(copy, replaced), "WithLoadout must create a new snapshot.");
            Check(replaced.UnitIds[0] == unitBefore && replaced.OrderIds[0] == orderBefore, "WithLoadout must clone input arrays.");
        }

        private static void ValidateInitialization(MetaDefinitions definitions, ProfileSnapshot seed)
        {
            FakeRepository repository = new FakeRepository(null);
            ProfileService service = new ProfileService(definitions, seed, repository);
            Expect<InvalidOperationException>(delegate { ProfileSnapshot unused = service.Current; }, "Current must fail before Initialize.");
            service.Initialize();
            service.Initialize();
            Check(repository.Writes == 1 && ReferenceEquals(service.Current, seed), "Initial profile must be saved exactly once when missing.");
        }

        private static void ValidateProfileRejections(MetaDefinitions definitions, ProfileSnapshot seed)
        {
            string[] duplicateUnits = Copy(seed.UnitIds);
            duplicateUnits[1] = duplicateUnits[0];
            Expect<InvalidDataException>(delegate { new ProfileService(definitions, seed.WithLoadout(duplicateUnits, Copy(seed.OrderIds)), new FakeRepository(null)); }, "Duplicate units must be rejected.");

            string[] missingOwned = Without(seed.OwnedIds, seed.UnitIds[0]);
            ProfileSnapshot missing = Create(seed, seed.CommanderLevel, seed.ArenaLevel, missingOwned, Copy(seed.UnitIds), Copy(seed.OrderIds));
            Expect<InvalidDataException>(delegate { new ProfileService(definitions, missing, new FakeRepository(null)); }, "Equipped content missing from ownership must be rejected.");

            ContentDefinition arenaLockedUnit = FindArenaLockedUnit(definitions, seed.ArenaLevel);
            string[] lockedUnits = Copy(seed.UnitIds);
            lockedUnits[0] = arenaLockedUnit.Id;
            ProfileSnapshot arenaLocked = Create(seed, seed.CommanderLevel, seed.ArenaLevel, AddIfMissing(seed.OwnedIds, arenaLockedUnit.Id), lockedUnits, Copy(seed.OrderIds));
            Expect<InvalidDataException>(delegate { new ProfileService(definitions, arenaLocked, new FakeRepository(null)); }, "Arena-locked unit must be rejected.");

            string[] wrongOrderType = Copy(seed.OrderIds);
            wrongOrderType[0] = MediumTankId;
            ProfileSnapshot orderType = Create(seed, 35, seed.ArenaLevel, seed.OwnedIds, Copy(seed.UnitIds), wrongOrderType);
            Expect<InvalidDataException>(delegate { new ProfileService(definitions, orderType, new FakeRepository(null)); }, "Unit id in an order slot must be rejected.");

            ProfileService service = new ProfileService(definitions, seed, new FakeRepository(seed));
            service.Initialize();
            Check(service.Equip(MediumTankId, -1) == EquipResult.InvalidSlot, "Negative unit slot must be rejected.");
        }

        private static void ValidateEquipBehavior(MetaDefinitions definitions, ProfileSnapshot levelThirtyFive)
        {
            FakeRepository repository = new FakeRepository(levelThirtyFive);
            ProfileService service = new ProfileService(definitions, levelThirtyFive, repository);
            service.Initialize();
            int changed = 0;
            service.Changed += delegate(ProfileSnapshot profile) { changed++; };
            ProfileSnapshot before = service.Current;
            Check(service.Equip(MediumTankId, 0) == EquipResult.Success, "unit.medium_tank must equip into slot 0.");
            Check(!ReferenceEquals(before, service.Current) && before.UnitIds[0] != service.Current.UnitIds[0], "Successful equip must replace only the current snapshot.");
            Check(repository.Writes == 1 && changed == 1, "Successful equip must save once and notify once.");
            Check(service.Equip(MediumTankId, 1) == EquipResult.AlreadyEquipped, "Equipping the same unit to another slot must be rejected.");
            Check(repository.Writes == 1 && changed == 1, "Rejected duplicate must not save or notify.");

            FakeRepository failing = new FakeRepository(levelThirtyFive);
            failing.FailWrites = true;
            ProfileService failingService = new ProfileService(definitions, levelThirtyFive, failing);
            failingService.Initialize();
            int failedChanged = 0;
            failingService.Changed += delegate(ProfileSnapshot profile) { failedChanged++; };
            ProfileSnapshot failedBefore = failingService.Current;
            Check(failingService.Equip(MediumTankId, 0) == EquipResult.SaveFailed, "IOException during save must return SaveFailed.");
            Check(ReferenceEquals(failedBefore, failingService.Current) && failedChanged == 0, "Failed save must retain Current and not notify.");
        }

        private static void ValidateOrderLocks(MetaDefinitions definitions, ProfileSnapshot seed, ProfileSnapshot levelThirtyFive)
        {
            ProfileService lowLevel = new ProfileService(definitions, seed, new FakeRepository(seed));
            lowLevel.Initialize();
            Check(!lowLevel.IsOrderSlotUnlocked(1) && !lowLevel.IsOrderSlotUnlocked(2), "Order slots 1 and 2 must remain locked before commander levels 20 and 35.");
            Check(lowLevel.Equip(SmokeOrderId, 1) == EquipResult.SlotLocked, "A locked order slot must reject equip.");

            ProfileService highLevel = new ProfileService(definitions, levelThirtyFive, new FakeRepository(levelThirtyFive));
            highLevel.Initialize();
            Check(highLevel.IsOrderSlotUnlocked(1) && highLevel.IsOrderSlotUnlocked(2), "Order slots 1 and 2 must unlock at commander level 35.");
            Check(levelThirtyFive.CommanderLevel == 35 && levelThirtyFive.PlayerName == seed.PlayerName && levelThirtyFive.OwnedIds.Count == seed.OwnedIds.Count, "Level 35 fixture must preserve seed values and ownership.");
        }

        private static void ValidateDiskRepository(MetaDefinitions definitions, ProfileSnapshot fixture)
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string root = Path.Combine(projectRoot, "Logs", "TankDraftSetup", "ProfileValidation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "profile.json");
            JsonProfileRepository repository = new JsonProfileRepository(path);
            repository.Save(fixture);

            ProfileService writer = new ProfileService(definitions, fixture, repository);
            writer.Initialize();
            Check(writer.Equip(MediumTankId, 0) == EquipResult.Success, "Disk-backed service must save equipped medium tank.");
            ProfileService restarted = new ProfileService(definitions, fixture, new JsonProfileRepository(path));
            restarted.Initialize();
            Check(restarted.Current.UnitIds[0] == MediumTankId && Contains(restarted.Current.OrderIds, RepairOrderId) && Contains(restarted.Current.OrderIds, SmokeOrderId), "Restarted service must retain unit and order loadout.");

            string valid = File.ReadAllText(path);
            ExpectInvalidFile(repository, path, valid.Substring(0, valid.Length - 1));
            ExpectInvalidFile(repository, path, ReplaceRequired(valid, "\"version\":1", "\"version\":2"));
            ExpectInvalidFile(repository, path, ReplaceRequired(valid, "\"mastery\":" + fixture.Mastery + ",", string.Empty));
            ExpectInvalidFile(repository, path, ReplaceRequired(valid, "\"energy\":" + fixture.Energy, "\"energy\":\"wrong\""));
            ExpectInvalidFile(repository, path, ReplaceRequired(valid, "\"version\":1", "\"version\":1,\"version\":1"));
        }

        private static void ExpectInvalidFile(JsonProfileRepository repository, string path, string invalid)
        {
            File.WriteAllText(path, invalid);
            Expect<InvalidDataException>(delegate { repository.Load(); }, "Invalid disk profile must be rejected.");
            Check(File.ReadAllText(path) == invalid, "Invalid profile file must not be overwritten while loading.");
        }

        private static ProfileSnapshot CreateLevelThirtyFiveFixture(MetaDefinitions definitions, ProfileSnapshot seed)
        {
            string[] units = Copy(seed.UnitIds);
            int mediumIndex = IndexOf(units, MediumTankId);
            if (mediumIndex >= 0)
            {
                string replacement = FindOwnedUnequippedUnit(definitions, seed.OwnedIds, units, MediumTankId);
                units[mediumIndex] = replacement;
            }

            string[] orders = Copy(seed.OrderIds);
            orders[0] = RepairOrderId;
            orders[1] = SmokeOrderId;
            if (orders[2] == RepairOrderId || orders[2] == SmokeOrderId) orders[2] = string.Empty;
            return Create(seed, 35, seed.ArenaLevel, Copy(seed.OwnedIds), units, orders);
        }

        private static string FindOwnedUnequippedUnit(MetaDefinitions definitions, System.Collections.Generic.IReadOnlyList<string> owned, System.Collections.Generic.IReadOnlyList<string> equipped, string excludedId)
        {
            for (int index = 0; index < owned.Count; index++)
            {
                ContentDefinition definition;
                if (definitions.TryGet(owned[index], out definition) && definition.Kind == ContentKind.Unit && owned[index] != excludedId && !Contains(equipped, owned[index]))
                    return owned[index];
            }
            throw new InvalidOperationException("Seed must own an unequipped replacement unit for unit.medium_tank.");
        }

        private static ContentDefinition FindArenaLockedUnit(MetaDefinitions definitions, int arenaLevel)
        {
            for (int index = 0; index < definitions.Entries.Count; index++)
            {
                ContentDefinition definition = definitions.Entries[index];
                if (definition.Kind == ContentKind.Unit && definition.RequiredArena > arenaLevel) return definition;
            }
            throw new InvalidOperationException("Fixture catalog must contain a unit above the seed arena level.");
        }

        private static ProfileSnapshot Create(ProfileSnapshot source, int commanderLevel, int arenaLevel, System.Collections.Generic.IReadOnlyList<string> owned, string[] units, string[] orders)
        {
            return new ProfileSnapshot(source.PlayerName, commanderLevel, arenaLevel, source.ArenaProgress, source.Energy, source.Gems, source.Coins, source.Mastery, Copy(owned), units, orders);
        }

        private static string[] AddIfMissing(System.Collections.Generic.IReadOnlyList<string> values, string id)
        {
            if (Contains(values, id)) return Copy(values);
            string[] copy = new string[values.Count + 1];
            for (int index = 0; index < values.Count; index++) copy[index] = values[index];
            copy[copy.Length - 1] = id;
            return copy;
        }

        private static string[] Without(System.Collections.Generic.IReadOnlyList<string> values, string id)
        {
            int removeIndex = IndexOf(values, id);
            if (removeIndex < 0) throw new InvalidOperationException("Expected owned fixture id is absent: " + id);
            string[] copy = new string[values.Count - 1];
            for (int source = 0, target = 0; source < values.Count; source++) if (source != removeIndex) copy[target++] = values[source];
            return copy;
        }

        private static string[] Copy(System.Collections.Generic.IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = values[index];
            return copy;
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<string> values, string id) { return IndexOf(values, id) >= 0; }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> values, string id)
        {
            for (int index = 0; index < values.Count; index++) if (values[index] == id) return index;
            return -1;
        }

        private static string ReplaceRequired(string source, string oldValue, string newValue)
        {
            string replaced = source.Replace(oldValue, newValue);
            if (replaced == source) throw new InvalidOperationException("Fixture JSON did not contain expected fragment: " + oldValue);
            return replaced;
        }

        private static void Expect<TException>(Action action, string message) where TException : Exception
        {
            try { action(); }
            catch (TException) { passed++; return; }
            throw new InvalidOperationException(message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed++;
        }

        private sealed class FakeRepository : IProfileRepository
        {
            private readonly ProfileSnapshot loaded;
            public int Writes { get; private set; }
            public bool FailWrites;
            public ProfileSnapshot LastSaved { get; private set; }

            public FakeRepository(ProfileSnapshot loaded) { this.loaded = loaded; }
            public ProfileSnapshot Load() { return loaded; }
            public void Save(ProfileSnapshot profile)
            {
                Writes++;
                if (FailWrites) throw new IOException("Expected validation write failure.");
                LastSaved = profile;
            }
        }
    }
}
