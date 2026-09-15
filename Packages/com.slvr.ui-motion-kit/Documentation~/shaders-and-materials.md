# Шейдеры и material variants

## Шейдеры

- `SLVR/UI/Shimmer` — диагональная полоса с color, width, softness, angle, intensity, phase и speed.
- `SLVR/UI/GradientFlow` — мягкий двухцветный gradient flow для CTA, progress, rarity frame и selected tab.
- `SLVR/UI/SoftGlowOverlay` — additive child-overlay без GrabPass, blur и offscreen texture.

Каждый шейдер сохраняет alpha исходного sprite/atlas sample, использует `_TextureSampleAdd`, стандартные
UGUI stencil properties, `UNITY_UI_CLIP_RECT` для `RectMask2D` и `UNITY_UI_ALPHACLIP` для alpha clip.

## Runtime использование

Добавьте `UiShaderEffectGraphic` на объект с `Graphic`, выберите descriptor и назначьте settings asset либо
provider. Компонент получает material lease при `OnEnable`, назначает shared material и возвращает исходный
material при `OnDisable`/`OnDestroy`. Для динамической конфигурации используйте `SetDescriptor`,
`SetSettingsProvider` и `RefreshMaterial`; изменять shared material напрямую нельзя.

Для ручной интеграции без компонента используйте `UiShaderMaterialCache.Acquire(...)` и обязательно вызовите
`Dispose()` у `UiShaderMaterialLease` после снятия material с `Graphic`.

## Quality и accessibility

Ambient phase отключается, если quality tier равен Low, `AmbientShadersEnabled` выключен, включён Reduced
Motion или Disable Flashes. Статичный вариант сохраняет заданную phase и имеет `_Speed = 0`; отдельный CPU
loop не создаётся.

## Batching и draw calls

- Graphics с одинаковым normalized descriptor и settings policy разделяют один base material и могут
  батчиться при остальных совместимых Canvas-условиях.
- Разные colors, phase, speed или effect parameters создают разные variants и разделяют batches. Для списков
  используйте общий descriptor; индивидуальная phase повышает стоимость batching.
- UGUI `Mask` создаёт derived stencil materials. Разные stencil depth или mask state обычно разделяют draw calls,
  даже если base variant общий. Cache владеет base variant, а stencil lifecycle остаётся за UGUI.
- `RectMask2D` использует clip rect без stencil material, но clipping state всё равно должен проверяться на
  целевом Canvas и платформе.
- `SoftGlowOverlay` требует child Graphic и добавляет draw/overdraw. Ограничивайте rect размером glow sprite;
  не используйте полноэкранные прозрачные overlay без профилирования.
- Шейдеры не используют GrabPass, blur или per-frame material instantiation.

Template materials в `Runtime/Resources` удерживают shaders в player build. Runtime не изменяет эти assets,
а создаёт и уничтожает только скрытые shared variants.
