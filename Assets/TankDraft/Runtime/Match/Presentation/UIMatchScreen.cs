using System;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Presentation
{
    public sealed class UIMatchScreen : UIScreen
    {
        [SerializeField]
        private UIMatchHudWindow _hud;
        [SerializeField]
        private UIMatchDraftWindow _draft;
        public void Initialize(Action<int> choose, Action order, Action next, Action menu)
        {
            Validate();
            Unbind();
            _hud.Initialize(next, menu);
            _draft.Initialize(choose, order);
        }

        public void Render(MatchViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Validate();
            _hud.Render(model);
            _draft.Render(model);
            Show();
        }

        public void Unbind()
        {
            if (_hud != null)
                _hud.Unbind();
            if (_draft != null)
                _draft.Unbind();
        }

        public void Validate()
        {
            if (_hud == null || _draft == null)
                throw new InvalidOperationException(name + " has incomplete match windows.");
            _hud.Validate();
            _draft.Validate();
        }

        private void OnDestroy() => Unbind();
    }
}
