using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Explicit transform that may be animated without fighting layout components.</summary>
    [DisallowMultipleComponent]
    public sealed class UiVisualRoot : MonoBehaviour
    {
        [SerializeField] private RectTransform visualTransform;

        public RectTransform VisualTransform
        {
            get
            {
                if (visualTransform == null)
                {
                    visualTransform = transform as RectTransform;
                }

                return visualTransform;
            }
        }

        public bool IsLayoutControlled => IsControlledByLayout(VisualTransform);

        public static bool IsControlledByLayout(RectTransform target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.GetComponent<ContentSizeFitter>() != null || target.GetComponent<AspectRatioFitter>() != null)
            {
                return true;
            }

            Transform parent = target.parent;
            if (parent == null)
            {
                return false;
            }

            return parent.GetComponent<HorizontalLayoutGroup>() != null
                || parent.GetComponent<VerticalLayoutGroup>() != null
                || parent.GetComponent<GridLayoutGroup>() != null;
        }
    }
}
