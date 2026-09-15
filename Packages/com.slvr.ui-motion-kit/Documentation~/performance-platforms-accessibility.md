# Performance, Android, Steam и accessibility

## Android

- Профилируйте release-like Development build на целевом mid-range device, отдельно при 60 Hz и 30 FPS cap.
- После warm-up фиксируйте CPU, GC.Alloc, Canvas.BuildBatch, batches, overdraw и memory delta.
- Low quality ограничивает currency icons, ambient shaders и loops; decorative graphics должны иметь `raycastTarget=false`.
- Optimized Frame Pacing проверяется вручную; package не меняет project setting.

## Steam PC

Проверяйте hover, pointer leave/cancel, focus visibility, navigation и submit одним и тем же компонентом. Hover никогда не является обязательным для touch. Mouse smoke должен выполняться и с legacy `StandaloneInputModule` (`pointerId = -1`), и с `InputSystemUIInputModule`, где mouse/pen используют положительный device id.

## Accessibility

Reduced Motion заменяет translation/rotation/shake/large scale на fade/immediate state. Disable Flashes запрещает shimmer/color flash. Intensity 0 приводит к понятному final state. Ошибка, selection, reward и progress milestone не должны передаваться только движением или цветом.

Benchmark panel показывает activity counts, но не объявляет GC PASS. Статус PASS допустим только по Profiler capture после warm-up; Editor Test Framework allocations не являются runtime steady-state measurement.
