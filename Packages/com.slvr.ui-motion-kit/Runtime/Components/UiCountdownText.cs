using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Lightweight realtime countdown presenter. It is independent from Time.timeScale, updates the
    /// label only when the displayed second changes and raises completion once so the host can refresh
    /// server-authoritative state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class UiCountdownText : MonoBehaviour
    {
        [SerializeField] private TMP_Text target;

        private string formatTemplate = "{0}";
        private Action completed;
        private double deadline;
        private int displayedSeconds = int.MinValue;
        private bool isRunning;
        private bool completionRaised;

        public void Bind(int secondsRemaining, string template, Action onCompleted = null)
        {
            ResolveTarget();
            formatTemplate = string.IsNullOrEmpty(template) ? "{0}" : template;
            completed = onCompleted;
            deadline = Time.realtimeSinceStartupAsDouble + Math.Max(0, secondsRemaining);
            displayedSeconds = int.MinValue;
            completionRaised = false;
            isRunning = secondsRemaining > 0;
            RenderNow();
        }

        public void Stop(bool clearText = false)
        {
            isRunning = false;
            completed = null;
            completionRaised = false;
            displayedSeconds = int.MinValue;
            if (clearText)
            {
                ResolveTarget();
                if (target != null)
                {
                    target.text = string.Empty;
                }
            }
        }

        public static string FormatClock(int totalSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";
        }

        private void Awake()
        {
            ResolveTarget();
        }

        private void OnEnable()
        {
            if (isRunning)
            {
                RenderNow();
            }
        }

        private void Update()
        {
            if (isRunning)
            {
                RenderNow();
            }
        }

        private void RenderNow()
        {
            ResolveTarget();
            int remaining = isRunning
                ? Math.Max(0, (int)Math.Ceiling(deadline - Time.realtimeSinceStartupAsDouble))
                : 0;

            if (remaining != displayedSeconds)
            {
                displayedSeconds = remaining;
                if (target != null)
                {
                    target.text = Format(formatTemplate, FormatClock(remaining));
                }
            }

            if (isRunning && remaining <= 0)
            {
                isRunning = false;
                if (!completionRaised)
                {
                    completionRaised = true;
                    Action callback = completed;
                    completed = null;
                    callback?.Invoke();
                }
            }
        }

        private void ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponent<TMP_Text>();
            }
        }

        private static string Format(string template, string countdown)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, countdown);
            }
            catch (FormatException)
            {
                return countdown;
            }
        }
    }
}
