namespace TankDraft.Contracts
{
    public enum EquipResult
    {
        Success,
        UnknownContent,
        NotOwned,
        ArenaLocked,
        SlotLocked,
        InvalidSlot,
        AlreadyEquipped,
        SaveFailed
    }

    public interface IProfileRepository
    {
        ProfileSnapshot Load();
        void Save(ProfileSnapshot profile);
    }
}
