# Gate 3 — локальная Editor-проверка

Дата: `2026-07-21`  
Среда: Unity `6000.3.10f1`, Windows DX12 Editor  
Scope: shader material lifecycle, steady shader frames, Mask/RectMask2D, notification badge lifecycle.

## Результаты

- EditMode: `26/26`.
- PlayMode: `30/30`.
- Три shaders поддерживаются текущим graphics API и компилируются без ошибок.
- Mask smoke: custom Shimmer graphic не рисуется за stencil mask.
- RectMask2D smoke: custom GradientFlow graphic не рисуется за clip rect.
- Material variants: cache count стабилен на 12 GPU-animation frames; disable/destroy возвращает count к baseline.
- Canvas recorder `Canvas.BuildBatch` доступен; после warm-up последний steady sample равен `0`.
- Smoke использует два ограниченных `80x80` mask rect. Soft glow не участвовал в capture; по контракту каждый
  `SoftGlowOverlay` child добавляет один прозрачный overlay draw и соответствующий локальный overdraw.

## Ограничения измерения

`GC.Alloc` внутри Unity Test Framework показал последний sample `10600` bytes. В sample входят coroutine/test
runner, assertion и logging allocations, поэтому значение не является доказательством package steady-state GC
и не используется как PASS/FAIL. Runtime shader path не имеет component `Update` и не изменяет material по кадрам.

Android-устройство не подключено (`adb devices` пуст). Android Low-quality Profiler capture, фактический GPU
overdraw и device steady-state GC остаются обязательной внешней частью полного Gate 3.
