using System;

namespace SLVR.UIMotion
{
    /// <summary>Host-overridable source of current motion settings.</summary>
    public interface IUiMotionSettingsProvider
    {
        UiMotionSettingsSnapshot Current { get; }
        event Action Changed;
    }
}
