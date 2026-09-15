using UnityEngine;

namespace Vareiko.Foundation.UI
{
    public class UIWindow : UIElement
    {
        [SerializeField] private bool _isModal = true;
        [SerializeField] private int _defaultPriority;

        public bool IsModal => _isModal;
        public int DefaultPriority => _defaultPriority;
    }
}
