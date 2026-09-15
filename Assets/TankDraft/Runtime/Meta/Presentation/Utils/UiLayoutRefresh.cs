using System;
using UnityEngine;
using UnityEngine.UI;

namespace TankDraft.UI
{
    public sealed class UiLayoutRefresh : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private LayoutGroup[] _groups;

        private bool _isDirty;

        public void Rebuild()
        {
            ValidateReferences();
            try
            {
                SetGroupsEnabled(true);
                // Each group is a layout root; plain RectTransform ancestors stop UGUI traversal.
                for (var index = _groups.Length - 1; index >= 0; index--)
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_groups[index].transform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
            }
            finally
            {
                SetGroupsEnabled(false);
            }
        }

        private void OnEnable()
        {
            _isDirty = true;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (UnityEngine.Application.isPlaying)
                _isDirty = true;
        }

        private void LateUpdate()
        {
            if (!_isDirty)
                return;

            _isDirty = false;
            Rebuild();
        }

        private void ValidateReferences()
        {
            if (_root == null || _groups == null)
                throw new InvalidOperationException($"{name} requires a root and authored layout groups.");
            foreach (var group in _groups)
            {
                if (group == null)
                    throw new InvalidOperationException($"{name} has an unassigned layout group.");
            }
        }

        private void SetGroupsEnabled(bool enabled)
        {
            foreach (var group in _groups)
                group.enabled = enabled;
        }
    }
}

