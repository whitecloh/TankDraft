using UnityEngine;
using TankDraft.Contracts;
namespace TankDraft.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Content/New profile")]
    public sealed class NewProfileAsset : ScriptableObject
    {
        [SerializeField] private string _playerName;
        [SerializeField] private int _commanderLevel = 1, _arenaLevel = 1, _arenaProgress, _energy, _gems, _coins, _mastery;
        [SerializeField] private string[] _ownedIds, _unitIds, _orderIds;
        public ProfileSnapshot CreateSnapshot() => new ProfileSnapshot(_playerName,_commanderLevel,_arenaLevel,_arenaProgress,_energy,_gems,_coins,_mastery,_ownedIds,_unitIds,_orderIds);
    }
}
