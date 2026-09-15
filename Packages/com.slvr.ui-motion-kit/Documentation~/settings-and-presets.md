# Settings and presets

Порядок fallback: component override → preset → theme → `UiMotionDefaults`.

`UiMotionSettingsProvider` хранит только runtime state. Сохранение пользовательских настроек
реализует host project через собственный persistence layer; пакет не обращается к PlayerPrefs.

Reduced Motion заменяет translation/scale/rotation/shake на fade, а shimmer и particle burst — на
immediate state. Интенсивность `0` всегда означает immediate state.
