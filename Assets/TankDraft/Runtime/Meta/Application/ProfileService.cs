using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TankDraft.Contracts;

namespace TankDraft.Application
{
    public sealed class ProfileService
    {
        private readonly MetaDefinitions definitions;
        private readonly ProfileSnapshot initial;
        private readonly IProfileRepository repository;
        private readonly IServerProfileClient server;
        private readonly SemaphoreSlim serverGate = new SemaphoreSlim(1, 1);
        private ProfileSnapshot current;
        private bool initialized;

        public ProfileService(MetaDefinitions definitions, ProfileSnapshot initial, IProfileRepository repository, IServerProfileClient server = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            this.definitions = definitions;
            this.initial = initial;
            this.repository = repository;
            this.server = server;
            ValidateSnapshot(initial);
        }

        public event Action<ProfileSnapshot> Changed;

        public bool IsServerBacked { get { return server != null; } }

        public ProfileSnapshot Current
        {
            get
            {
                if (!initialized) throw new InvalidOperationException("ProfileService must be initialized before reading Current.");
                return current;
            }
        }

        public void Initialize()
        {
            if (initialized) return;

            ProfileSnapshot loaded = repository.Load();
            if (loaded == null)
            {
                repository.Save(initial);
                current = initial;
            }
            else
            {
                ValidateSnapshot(loaded);
                current = loaded;
            }

            initialized = true;
        }

        // A server profile is already validated as a complete immutable snapshot. It
        // never writes the local QA repository, which remains a legacy-only store.
        public void InitializeServer(ProfileSnapshot snapshot)
        {
            if (initialized) throw new InvalidOperationException("ProfileService is already initialized.");
            ValidateSnapshot(snapshot);
            current = snapshot;
            initialized = true;
        }

        public EquipResult Equip(string contentId, int slot)
        {
            EnsureInitialized();

            if (server != null) throw new InvalidOperationException("Server-backed profiles require EquipAsync.");
            ProfileSnapshot candidate;
            EquipResult validation = TryCreateEquipCandidate(contentId, slot, out candidate);
            if (validation != EquipResult.Success) return validation;

            try
            {
                repository.Save(candidate);
            }
            catch (IOException)
            {
                return EquipResult.SaveFailed;
            }
            catch (UnauthorizedAccessException)
            {
                return EquipResult.SaveFailed;
            }

            ReplaceCurrent(candidate);
            return EquipResult.Success;
        }

        public async Task<EquipResult> EquipAsync(string contentId, int slot, CancellationToken token)
        {
            EnsureInitialized();
            if (server == null) return Equip(contentId, slot);

            await serverGate.WaitAsync(token);
            try
            {
                ProfileSnapshot candidate;
                EquipResult validation = TryCreateEquipCandidate(contentId, slot, out candidate);
                if (validation != EquipResult.Success) return validation;

                try
                {
                    ProfileSnapshot saved = await server.SaveLoadoutAsync(Copy(candidate.UnitIds), Copy(candidate.OrderIds), token);
                    if (saved == null || !SameLoadout(saved, candidate)) return EquipResult.SaveFailed;
                    ValidateSnapshot(saved);
                    ReplaceCurrent(saved);
                    return EquipResult.Success;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ServerProfileRefreshRequiredException refresh)
                {
                    ValidateSnapshot(refresh.Snapshot);
                    ReplaceCurrent(refresh.Snapshot);
                    return EquipResult.SaveFailed;
                }
                catch (Exception)
                {
                    return EquipResult.SaveFailed;
                }
            }
            finally { serverGate.Release(); }
        }

        public async Task RefreshAsync(CancellationToken token)
        {
            EnsureInitialized();
            if (server == null) throw new InvalidOperationException("ProfileService is not server-backed.");

            await serverGate.WaitAsync(token);
            try
            {
                ProfileSnapshot refreshed = await server.GetAsync(token);
                if (refreshed == null) throw new InvalidDataException("Server refresh returned no profile.");
                ValidateSnapshot(refreshed);
                ReplaceCurrent(refreshed);
            }
            finally { serverGate.Release(); }
        }

        private EquipResult TryCreateEquipCandidate(string contentId, int slot, out ProfileSnapshot candidate)
        {
            candidate = null;

            ContentDefinition definition;
            if (!definitions.TryGet(contentId, out definition)) return EquipResult.UnknownContent;
            if (!IsValidSlot(definition.Kind, slot)) return EquipResult.InvalidSlot;
            if (definition.Kind == ContentKind.Order && !IsOrderSlotUnlocked(slot)) return EquipResult.SlotLocked;

            EquipResult availability = GetAvailability(contentId);
            if (availability != EquipResult.Success) return availability;

            string[] units = Copy(current.UnitIds);
            string[] orders = Copy(current.OrderIds);
            string[] loadout = definition.Kind == ContentKind.Unit ? units : orders;

            if (loadout[slot] == contentId) return EquipResult.AlreadyEquipped;
            for (int index = 0; index < loadout.Length; index++)
            {
                if (loadout[index] == contentId) return EquipResult.AlreadyEquipped;
            }

            loadout[slot] = contentId;
            candidate = current.WithLoadout(units, orders);
            return EquipResult.Success;
        }

        private void ReplaceCurrent(ProfileSnapshot snapshot)
        {
            current = snapshot;
            Action<ProfileSnapshot> changed = Changed;
            if (changed != null) changed(snapshot);
        }

        public EquipResult GetAvailability(string id)
        {
            EnsureInitialized();
            ContentDefinition definition;
            if (!definitions.TryGet(id, out definition)) return EquipResult.UnknownContent;
            if (current != null && definition.RequiredArena > current.ArenaLevel) return EquipResult.ArenaLocked;
            if (current != null && !Contains(current.OwnedIds, id)) return EquipResult.NotOwned;
            return EquipResult.Success;
        }

        public bool IsOrderSlotUnlocked(int slot)
        {
            EnsureInitialized();
            if (slot < 0 || slot >= definitions.Rules.OrderSlots) return false;
            return current.CommanderLevel >= definitions.Rules.OrderUnlockLevels[slot];
        }

        private void ValidateSnapshot(ProfileSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ValidateOwned(snapshot);
            ValidateLoadout(snapshot, snapshot.UnitIds, ContentKind.Unit);
            ValidateLoadout(snapshot, snapshot.OrderIds, ContentKind.Order);
        }

        private void ValidateOwned(ProfileSnapshot snapshot)
        {
            for (int index = 0; index < snapshot.OwnedIds.Count; index++)
            {
                string id = snapshot.OwnedIds[index];
                ContentDefinition definition;
                if (!definitions.TryGet(id, out definition)) throw new InvalidDataException("Profile owns unknown content: " + id);
                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (snapshot.OwnedIds[earlier] == id) throw new InvalidDataException("Profile owns duplicate content: " + id);
                }
            }
        }

        private void ValidateLoadout(ProfileSnapshot snapshot, System.Collections.Generic.IReadOnlyList<string> ids, ContentKind expectedKind)
        {
            int expectedSlots = expectedKind == ContentKind.Unit ? definitions.Rules.UnitSlots : definitions.Rules.OrderSlots;
            if (ids.Count != expectedSlots) throw new InvalidDataException("Profile has an invalid " + expectedKind + " loadout size.");

            for (int index = 0; index < ids.Count; index++)
            {
                string id = ids[index];
                if (id.Length == 0)
                {
                    if (expectedKind == ContentKind.Unit) throw new InvalidDataException("Unit slots cannot be empty.");
                    continue;
                }

                if (expectedKind == ContentKind.Order && snapshot.CommanderLevel < definitions.Rules.OrderUnlockLevels[index])
                    throw new InvalidDataException("A locked order slot must be empty.");

                ContentDefinition definition;
                if (!definitions.TryGet(id, out definition) || definition.Kind != expectedKind)
                    throw new InvalidDataException("Profile loadout has unknown or mismatched content: " + id);
                if (!Contains(snapshot.OwnedIds, id)) throw new InvalidDataException("Profile equips content it does not own: " + id);
                if (definition.RequiredArena > snapshot.ArenaLevel) throw new InvalidDataException("Profile equips arena-locked content: " + id);

                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (ids[earlier] == id) throw new InvalidDataException("Profile loadout contains duplicate content: " + id);
                }
            }
        }

        private bool IsValidSlot(ContentKind kind, int slot)
        {
            int slotCount = kind == ContentKind.Unit ? definitions.Rules.UnitSlots : definitions.Rules.OrderSlots;
            return slot >= 0 && slot < slotCount;
        }

        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("ProfileService must be initialized first.");
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<string> values, string value)
        {
            for (int index = 0; index < values.Count; index++) if (values[index] == value) return true;
            return false;
        }

        private static string[] Copy(System.Collections.Generic.IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = values[index];
            return copy;
        }

        private static bool SameLoadout(ProfileSnapshot left, ProfileSnapshot right)
        {
            return SameIds(left.UnitIds, right.UnitIds) && SameIds(left.OrderIds, right.OrderIds);
        }

        private static bool SameIds(System.Collections.Generic.IReadOnlyList<string> left, System.Collections.Generic.IReadOnlyList<string> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++) if (left[index] != right[index]) return false;
            return true;
        }
    }
}
