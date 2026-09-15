using UnityEngine;
namespace TankDraft.UI
{
    public sealed class UIMainMenuRuntimeRoot : MonoBehaviour
    {
        [SerializeField] private UIMainMenuScreen _screen;
        [SerializeField] private UIMainMenuHudWindow _hud;
        [SerializeField] private UIMainMenuArenaWindow _arena;
        [SerializeField] private UIMainMenuCollectionWindow _collection;
        [SerializeField] private UICardDetailsWindow _details;
        [SerializeField] private UIMessageWindow _message;
        [SerializeField] private UIProgressionWindow _progression;
        public UIMainMenuScreen Screen => _screen;
        public UIMainMenuHudWindow Hud => _hud;
        public UIMainMenuArenaWindow Arena => _arena;
        public UIMainMenuCollectionWindow Collection => _collection;
        public UICardDetailsWindow Details => _details;
        public UIMessageWindow Message => _message;
        public UIProgressionWindow Progression => _progression;
        public void Validate()
        {
            if (!_screen || !_hud || !_arena || !_collection || !_details || !_message)
                throw new System.InvalidOperationException("UI runtime root has unassigned authored views.");
        }
    }
}
