using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UICatalogPanel : UIPanel
    {
        [SerializeField] private UICatalogCardView _template;
        [SerializeField] private RectTransform _content;
        [SerializeField] private GridLayoutGroup _group;

        private Action<string> _onClick;
        private UiViewPool<UICatalogCardView> _pool;
        private bool _layoutDirty;

        public void Initialize(Action<string> onClick)
        {
            ValidateReferences();
            _template.gameObject.SetActive(false);
            EnsurePool();
            _pool.Clear();
            _onClick = onClick;
        }

        public void Bind(CardViewModel[] cards)
        {
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            ValidateReferences();
            EnsurePool();
            _pool.Sync(cards.Length, (item, index) =>
            {
                CardViewModel card = cards[index];
                if (card == null)
                    throw new ArgumentException("Catalog cards cannot contain null.", nameof(cards));

                string id = card.Id;
                item.Bind(card);
                item.SetClickAction(() =>
                {
                    if (!string.IsNullOrEmpty(id))
                        _onClick?.Invoke(id);
                });
            });
            RebuildGrid();
        }

        public void Clear()
        {
            _pool?.Clear();
        }

        private void OnDestroy()
        {
            _pool?.Dispose();
            _pool = null;
        }

        private void EnsurePool()
        {
            if (_pool != null)
                return;

            _template.gameObject.SetActive(false);
            _pool = new UiViewPool<UICatalogCardView>(_template, _content, ClearItem);
        }

        private void RebuildGrid()
        {
            _group.enabled = true;
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                float preferredHeight = LayoutUtility.GetPreferredHeight(_content);
                if (!Mathf.Approximately(_content.rect.height, preferredHeight))
                    _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            }
            finally
            {
                _group.enabled = false;
            }
        }

        private static void ClearItem(UICatalogCardView item)
        {
            item.Clear();
        }

        private void ValidateReferences()
        {
            if (_template == null || _content == null || _group == null)
                throw new InvalidOperationException($"{name} requires a template, content rect, and grid layout group.");
        }

        private void OnRectTransformDimensionsChange()
        {
            if (UnityEngine.Application.isPlaying)
                _layoutDirty = true;
        }

        private void LateUpdate()
        {
            if (!_layoutDirty || !isActiveAndEnabled)
                return;

            _layoutDirty = false;
            RebuildGrid();
        }
    }

    internal sealed class UiViewPool<T> : IDisposable where T : Component
    {
        private readonly T _template;
        private readonly Transform _parent;
        private readonly Action<T> _clear;
        private readonly List<T> _instances = new List<T>();

        public UiViewPool(T template, Transform parent, Action<T> clear)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _clear = clear ?? throw new ArgumentNullException(nameof(clear));
        }

        public void Sync(int count, Action<T, int> bind)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (bind == null)
                throw new ArgumentNullException(nameof(bind));

            for (var index = 0; index < count; index++)
            {
                T item = Acquire(index);
                bind(item, index);
                item.transform.SetSiblingIndex(index);
                item.gameObject.SetActive(true);
            }

            for (var index = count; index < _instances.Count; index++)
                Release(_instances[index]);
        }

        public void Clear()
        {
            for (var index = 0; index < _instances.Count; index++)
                Release(_instances[index]);
        }

        public void Dispose()
        {
            for (var index = 0; index < _instances.Count; index++)
            {
                T item = _instances[index];
                if (item == null)
                    continue;

                _clear(item);
                if (UnityEngine.Application.isPlaying)
                    UnityEngine.Object.Destroy(item.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
            }

            _instances.Clear();
        }

        private T Acquire(int index)
        {
            if (index < _instances.Count)
                return _instances[index];

            T item = UnityEngine.Object.Instantiate(_template, _parent);
            item.gameObject.SetActive(false);
            _instances.Add(item);
            return item;
        }

        private void Release(T item)
        {
            if (item == null)
                return;

            _clear(item);
            item.gameObject.SetActive(false);
        }
    }
}
