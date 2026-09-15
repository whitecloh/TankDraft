using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Coordinates an authored set of panel transitions as one reentrant window transition.
    /// The host can safely defer window deactivation until every panel has left the viewport.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPanelTransitionGroup : MonoBehaviour
    {
        [SerializeField] private UiPanelTransition[] transitions = Array.Empty<UiPanelTransition>();

        private Action completion;
        private Action[] pendingHandlers;
        private bool pendingShowing;
        private int pendingCount;
        private int operationVersion;

        public int TransitionCount => transitions?.Length ?? 0;

        public void Configure(UiPanelTransition[] authoredTransitions)
        {
            CancelPendingOperation();
            transitions = authoredTransitions ?? Array.Empty<UiPanelTransition>();
        }

        public void PrepareShow()
        {
            CancelPendingOperation();
            ForEachActiveSelf(static transition => transition.PrepareShow());
        }

        public void Show(Action onComplete = null)
        {
            BeginOperation(showing: true, immediate: false, onComplete);
        }

        public void Hide(Action onComplete = null)
        {
            BeginOperation(showing: false, immediate: false, onComplete);
        }

        public void ShowImmediate()
        {
            BeginOperation(showing: true, immediate: true, null);
        }

        public void HideImmediate()
        {
            BeginOperation(showing: false, immediate: true, null);
        }

        private void BeginOperation(bool showing, bool immediate, Action onComplete)
        {
            CancelPendingOperation();
            completion = onComplete;
            pendingShowing = showing;
            int version = operationVersion;
            if (showing)
            {
                RestoreInactiveTransitionsForShow();
            }

            pendingCount = CountValidTransitions();
            if (pendingCount == 0)
            {
                CompleteOperation(version);
                return;
            }

            pendingHandlers = new Action[transitions.Length];
            for (int i = 0; i < transitions.Length; i++)
            {
                UiPanelTransition transition = transitions[i];
                if (transition == null || !transition.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int transitionIndex = i;
                Action handler = null;
                handler = () =>
                {
                    if (showing)
                    {
                        transition.ShowCompleted -= handler;
                    }
                    else
                    {
                        transition.HideCompleted -= handler;
                    }

                    if (pendingHandlers != null && transitionIndex < pendingHandlers.Length)
                    {
                        pendingHandlers[transitionIndex] = null;
                    }

                    if (version != operationVersion)
                    {
                        return;
                    }

                    pendingCount -= 1;
                    if (pendingCount <= 0)
                    {
                        CompleteOperation(version);
                    }
                };
                pendingHandlers[i] = handler;

                if (showing)
                {
                    transition.ShowCompleted += handler;
                    if (immediate) transition.ShowImmediate();
                    else transition.Show();
                }
                else
                {
                    transition.HideCompleted += handler;
                    if (immediate) transition.HideImmediate();
                    else transition.Hide();
                }
            }
        }

        private int CountValidTransitions()
        {
            int count = 0;
            if (transitions == null)
            {
                return count;
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null && transitions[i].gameObject.activeInHierarchy)
                {
                    count += 1;
                }
            }

            return count;
        }

        private void ForEachActiveSelf(Action<UiPanelTransition> action)
        {
            if (transitions == null)
            {
                return;
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                UiPanelTransition transition = transitions[i];
                if (transition != null && transition.gameObject.activeSelf)
                {
                    action(transition);
                }
            }
        }

        private void RestoreInactiveTransitionsForShow()
        {
            if (transitions == null)
            {
                return;
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                UiPanelTransition transition = transitions[i];
                if (transition != null && !transition.gameObject.activeInHierarchy)
                {
                    // Content binding may deactivate an optional panel between PrepareShow and
                    // the first rendered frame. Restore its authored state so a later same-window
                    // activation does not reveal a stale offscreen transform.
                    transition.ShowImmediate();
                }
            }
        }

        private void CompleteOperation(int version)
        {
            if (version != operationVersion)
            {
                return;
            }

            pendingCount = 0;
            ClearPendingHandlers();
            Action callback = completion;
            completion = null;
            callback?.Invoke();
        }

        private void CancelPendingOperation()
        {
            ClearPendingHandlers();
            operationVersion += 1;
            pendingCount = 0;
            completion = null;
        }

        private void ClearPendingHandlers()
        {
            if (pendingHandlers == null || transitions == null)
            {
                pendingHandlers = null;
                return;
            }

            int count = Mathf.Min(pendingHandlers.Length, transitions.Length);
            for (int i = 0; i < count; i++)
            {
                Action handler = pendingHandlers[i];
                UiPanelTransition transition = transitions[i];
                if (handler == null || transition == null)
                {
                    continue;
                }

                if (pendingShowing) transition.ShowCompleted -= handler;
                else transition.HideCompleted -= handler;
            }

            pendingHandlers = null;
        }

        private void OnDisable()
        {
            CancelPendingOperation();
        }
    }
}
