using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vareiko.Foundation.UI
{
    public class UIRegistry : MonoBehaviour
    {
        [SerializeField] private List<UIElement> _elements = new List<UIElement>();
        [SerializeField] private bool _scanChildrenOnAwake = true;

        private readonly Dictionary<string, UIElement> _map = new Dictionary<string, UIElement>(StringComparer.Ordinal);

        public int Count => _map.Count;

        protected virtual void Awake()
        {
            BuildMap();
        }

        public bool TryGetElement(string elementId, out UIElement element)
        {
            if (!TryNormalizeId(elementId, out string normalized))
            {
                element = null;
                return false;
            }

            return _map.TryGetValue(normalized, out element);
        }

        public bool TryGetScreen(string screenId, out UIScreen screen)
        {
            screen = null;
            UIElement element;
            if (!TryGetElement(screenId, out element))
            {
                return false;
            }

            screen = element as UIScreen;
            return screen != null;
        }

        public bool TryGetWindow(string windowId, out UIWindow window)
        {
            window = null;
            UIElement element;
            if (!TryGetElement(windowId, out element))
            {
                return false;
            }

            window = element as UIWindow;
            return window != null;
        }

        public virtual void BuildMap()
        {
            _map.Clear();

            List<UIElement> explicitElements = ListPool.Get();
            CollectExplicitElements(explicitElements);
            for (int i = 0; i < explicitElements.Count; i++)
            {
                Register(explicitElements[i]);
            }

            ListPool.Release(explicitElements);

            if (_scanChildrenOnAwake)
            {
                UIElement[] elements = GetComponentsInChildren<UIElement>(true);
                for (int i = 0; i < elements.Length; i++)
                {
                    Register(elements[i]);
                }
            }
        }

        public IEnumerable<KeyValuePair<string, UIElement>> EnumerateElements()
        {
            return _map;
        }

        protected virtual void CollectExplicitElements(List<UIElement> collector)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                UIElement element = _elements[i];
                if (element != null)
                {
                    collector.Add(element);
                }
            }
        }

        protected void Register(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            if (!TryNormalizeId(element.Id, out string id))
            {
                if (element is UIScreen || element is UIWindow)
                {
                    throw new InvalidOperationException($"{element.GetType().Name} '{element.name}' must have a non-empty UI Id.");
                }

                return;
            }

            if (_map.TryGetValue(id, out UIElement existing))
            {
                if (ReferenceEquals(existing, element))
                {
                    return;
                }

                throw new InvalidOperationException($"Duplicate UIElement Id '{id}' registered by '{existing.name}' and '{element.name}'.");
            }

            _map[id] = element;
        }

        private static bool TryNormalizeId(string id, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                normalized = string.Empty;
                return false;
            }

            normalized = id.Trim();
            return normalized.Length > 0;
        }

        private static class ListPool
        {
            [ThreadStatic] private static List<UIElement> _cached;

            public static List<UIElement> Get()
            {
                if (_cached == null)
                {
                    return new List<UIElement>();
                }

                List<UIElement> list = _cached;
                _cached = null;
                return list;
            }

            public static void Release(List<UIElement> list)
            {
                list.Clear();
                _cached = list;
            }
        }
    }
}
