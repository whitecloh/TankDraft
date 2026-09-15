using System;
using System.Reflection;
using UnityEngine.EventSystems;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Classifies legacy and Input System pointer events without making the core package depend on
    /// com.unity.inputsystem. InputSystemUIInputModule uses positive device ids for mouse pointers,
    /// so the legacy pointerId >= 0 touch convention is only valid for base PointerEventData.
    /// </summary>
    internal static class UiPointerDeviceUtility
    {
        private static Type cachedDerivedEventType;
        private static PropertyInfo cachedTouchIdProperty;

        public static bool IsTouch(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return true;
            }

            Type eventType = eventData.GetType();
            if (eventType != typeof(PointerEventData))
            {
                CacheTouchIdProperty(eventType);
                if (cachedTouchIdProperty != null)
                {
                    object touchId = cachedTouchIdProperty.GetValue(eventData);
                    return touchId is int value && value > 0;
                }
            }

            return eventData.pointerId >= 0;
        }

        private static void CacheTouchIdProperty(Type eventType)
        {
            if (cachedDerivedEventType == eventType)
            {
                return;
            }

            cachedDerivedEventType = eventType;
            PropertyInfo property = eventType.GetProperty(
                "touchId",
                BindingFlags.Instance | BindingFlags.Public);
            cachedTouchIdProperty = property != null && property.PropertyType == typeof(int)
                ? property
                : null;
        }
    }
}
