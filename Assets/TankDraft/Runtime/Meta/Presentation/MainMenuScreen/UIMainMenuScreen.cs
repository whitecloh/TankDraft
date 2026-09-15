using System;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuScreen : UIScreen
    {
        [SerializeField] private UIMainMenuHudWindow _hudWindow;
        [SerializeField] private UIMainMenuArenaWindow _arenaWindow;
        [SerializeField] private UiLayoutRefresh _layoutRefresh;

        public void Initialize(IMainMenuUiCommands commands, Action openLastResult = null)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            ValidateReferences();
            Release();
            _hudWindow.Initialize(commands);
            _arenaWindow.Initialize(commands, openLastResult);
        }

        public void Bind(MainMenuViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _hudWindow.Bind(model);
            _arenaWindow.Bind(model);
            Show();
            _layoutRefresh.Rebuild();
        }

        public void Release()
        {
            if (_hudWindow != null)
                _hudWindow.Release();
            if (_arenaWindow != null)
                _arenaWindow.Release();
        }

        private void OnDestroy()
        {
            Release();
        }

        private void ValidateReferences()
        {
            if (_hudWindow == null || _arenaWindow == null || _layoutRefresh == null)
                throw new InvalidOperationException($"{name} has unassigned main menu references.");
        }
    }
}
