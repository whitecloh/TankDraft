# Performance baseline

Phase 1 проверяет:

- отсутствие глобальных cleanup-вызовов DOTween;
- не более одного tween на semantic channel;
- корректный kill/pause по Unity lifecycle;
- unscaled update при `Time.timeScale = 0`;
- default budget `1 primary / 3 secondary / 6 accent / 1 attention`.

Замеры steady-state GC, Canvas rebuild и burst effects выполняются в последующих фазах на Android.

## Phase 3.1 idle runtime

- `UiIdleMotion` не содержит `Update`, `LateUpdate` или coroutine loop;
- DOTween loop создаётся только после visibility/settings/budget admission;
- hide, disable и modal suppression уничтожают loop и освобождают budget lease;
- deterministic startup delay распределяет одинаковые элементы по фазе без `Random`;
- Low quality не допускает glow/shimmer ambient effects;
- Reduced Motion не допускает scale/translation/rotation/shimmer loops;
- runtime settings provider перезапускает admission по событию `Changed`.

Проверено Unity tests: EditMode `21/21`, PlayMode `18/18`. Android Profiler capture steady-state
GC и фактическая стоимость Canvas rebuild остаются частью полного Gate 3 после shader/badge scope.

## Phase 3.2 attention runtime

- `UiAttentionPulse` не содержит component `Update`, `LateUpdate` или coroutine loop;
- один scope допускает только один активный attention tween, остальные semantic key стоят в очереди;
- одинаковые `(owner, semantic key)` объединяются без роста очереди;
- пустые scope-state удаляются из общего runtime-словаря;
- Reduced Motion заменяет translation/scale/shake на короткий highlight ring;
- error fallback изменяется только по запросу и восстанавливается через `ClearErrorFallback()`.

Проверено Unity tests: EditMode `21/21`, PlayMode `22/22`. Android Profiler capture для burst-нагрузки
и проверка Canvas rebuild остаются частью полного Gate 3 после shader/badge scope.

## Phase 3.3 shader runtime

- shader phase вычисляется на GPU через `_Time`; CPU `Update` и per-frame material mutation отсутствуют;
- одинаковый normalized descriptor и settings policy используют один ref-counted material variant;
- последний lease удаляет variant из cache и уничтожает runtime material;
- Low quality, Reduced Motion и Disable Flashes создают статичный variant с `_Speed = 0`;
- `SoftGlowOverlay` добавляет отдельный UI draw и overdraw только там, где дизайнер добавил overlay Graphic;
- Mask может создавать отдельный UGUI stencil material на stencil depth; это ожидаемый batching split.

Shader compiler contract и базовый cache lifecycle проверяются EditMode tests. Mask screenshot, Canvas rebuild,
overdraw и Android capture остаются в Task 3.4 / полном Gate 3. Проверено Unity tests: EditMode `26/26`,
PlayMode `22/22`.

## Phase 3.4 badge and Editor Gate 3

- `UiNotificationBadge` создаёт tween только на zero→positive/increase либо при explicit urgent opt-in;
- rapid count updates заменяют Attention channel с текущего visual state;
- logical hide/disable уничтожает entrance/urgent tweens и освобождает Accent budget;
- steady shader animation не создаёт material variants и не вызывает CPU material mutation;
- Mask/RectMask2D pixel smoke и material cleanup проходят в PlayMode.

Проверено Unity tests: EditMode `26/26`, PlayMode `30/30`. Локальный `Canvas.BuildBatch` после warm-up: `0`
в последнем steady sample. Test Framework `GC.Alloc` не считается device evidence. Подробности:
[gate3-editor-profile.md](gate3-editor-profile.md). Android low-quality GC/overdraw gate ожидает устройство.
