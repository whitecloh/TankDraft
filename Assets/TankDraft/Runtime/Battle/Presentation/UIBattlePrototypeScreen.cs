using System;
using TMPro;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.BattlePresentation
{
    public sealed class UIBattlePrototypeScreen : UIScreen
    {
        [SerializeField]
        private TMP_Text _title, _status, _side0, _side1, _stats, _time, _scenarioLabel, _legend;
        [SerializeField]
        private UIButtonView _start, _pause, _step, _reset, _scenario, _debugZone;
        public UIButtonView StartButton => _start;
        public UIButtonView PauseButton => _pause;
        public UIButtonView StepButton => _step;
        public UIButtonView ResetButton => _reset;
        public UIButtonView ScenarioButton => _scenario;
        public UIButtonView DebugZoneButton => _debugZone;

        public void Initialize(Action start, Action pause, Action step, Action reset, Action next, Action zone, BattlePresentationSettings text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            Validate();
            Dispose();
            _title.text = text.Title;
            _legend.text = text.Legend;
            _start.SetClickAction(() => start?.Invoke());
            _pause.SetClickAction(() => pause?.Invoke());
            _step.SetClickAction(() => step?.Invoke());
            _reset.SetClickAction(() => reset?.Invoke());
            _scenario.SetClickAction(() => next?.Invoke());
            _debugZone.SetClickAction(() => zone?.Invoke());
            SetButtonText(_start, text.Start);
            SetButtonText(_pause, text.Pause);
            SetButtonText(_step, text.Step);
            SetButtonText(_reset, text.Reset);
            SetButtonText(_scenario, text.Scenario);
            SetButtonText(_debugZone, text.DebugZone);
            Show();
        }

        public void Render(string status, string side0, string side1, string stats, string time, string scenario, bool canStart, bool canPause, bool canStep, bool canZone)
        {
            Validate();
            _status.text = status;
            _side0.text = side0;
            _side1.text = side1;
            _stats.text = stats;
            _time.text = time;
            _scenarioLabel.text = scenario;
            _start.SetInteractable(canStart);
            _pause.SetInteractable(canPause);
            _step.SetInteractable(canStep);
            _debugZone.SetInteractable(canZone);
        }

        public void Unbind()
        {
            Clear(_start);
            Clear(_pause);
            Clear(_step);
            Clear(_reset);
            Clear(_scenario);
            Clear(_debugZone);
        }

        public void Dispose() => Unbind();
        public void Validate()
        {
            if (_title == null || _status == null || _side0 == null || _side1 == null || _stats == null || _time == null || _scenarioLabel == null || _legend == null || _start == null || _pause == null || _step == null || _reset == null || _scenario == null || _debugZone == null)
                throw new InvalidOperationException(name + " has unassigned battle HUD references.");
        }

        private void OnDestroy() => Unbind();
        private static void Clear(UIButtonView button)
        {
            if (button != null)
                button.ClearClickAction();
        }

        private static void SetButtonText(UIButtonView button, string value)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = value;
        }
    }
}