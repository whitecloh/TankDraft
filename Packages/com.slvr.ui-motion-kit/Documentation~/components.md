# Component reference

## Предупреждение

`UiWarningBannerMotion` — конечная анимация заданной хостом длительности: встречные ленты текста,
побуквенное появление заголовка/подзаголовка через `UiGlyphPopReveal`, вход и выход в fade.
В каждой ленте заранее подготовить по одному запасному элементу за каждым краем; `visibleSlots`
задаёт шаг повторения относительно ширины родителя. `Play(totalSeconds)` начинает показ заново;
`HideImmediate` и disable останавливают его и восстанавливают позиции. Reduced Motion оставляет
статичный текст с fade. Объекты в рантайме не создаются.
`SetPaused` останавливает/продолжает текущий timeline без перезапуска; хост предупреждения босса
передаёт фактическое состояние паузы, включая вложенное подтверждение выхода.
Хост может получить Bind до активации родительского окна: запуск timeline откладывается до
первого активного `LateUpdate`, после `Awake`/`OnEnable` компонентов. Иначе первый `Awake`
motion может сбросить timeline, а TMP ещё не имеет mesh. Сброс/семплирование неактивного текста
не должно вызывать `UpdateVertexData`; состояние применяется при следующем `OnPreRenderText`.

`UiGlyphPopReveal.Sample(0..1)` меняет TMP vertices/alpha: буквы по очереди уменьшаются от увеличенного
размера до обычного. Строка и локализационная привязка не заменяются. `OnPreRenderText` обновляет
переиспользуемые mesh-буферы после смены текста, шрифта или layout. После завершения reveal
повторные вызовы не выгружают вершины каждый кадр.

## Structural и interaction

- `UiAnimatedButton`: pointer/focus/submit feedback без повторного click.
- `UiHoverLiftMotion`: mouse/pen hover и keyboard/gamepad focus без `Update`.
  `LiftAndParallax` даёт scale/lift и optional event-driven parallax; `ScaleWave` не двигает hit-area,
  а плавно колеблет scale между `hoverScale` и `hoverScale + waveScaleDelta` и возвращается из
  фактического текущего scale без snap.
- `UiPanelTransition`, `UiModalBackdrop`, `UiScreenTransition`: reentrant visibility и корректные raycasts;
  `PrepareShow` заранее применяет hidden state и предотвращает flash финального состояния при активации popup;
  `RewardPop` допускает hidden scale до `0.05` и использует expand → undershoot → settle spring.
- `UiStagedReveal`: reusable последовательность fade/scale шагов с явной длительностью каждого шага,
  overshoot и accessibility fallback.
- `UiTypewriterText`: TMP reveal через `maxVisibleCharacters`; полный текст назначается один раз,
  поэтому wrapping остаётся стабильным и substring-аллокаций нет.
- `UiScrollRectMotion`: lifecycle-safe мгновенный/анимированный reset к authored normalized position, `CenterOn`
  для центрирования дочернего элемента и `PrepareReveal`/`PlayReveal` для мягкого top-to-bottom раскрытия
  существующего `RectMask2D`. Reveal анимирует только bottom padding маски, сохраняет authored softness/padding,
  не меняет viewport/content geometry и не добавляет per-frame idle logic. `revealInitialVisibleFraction`
  задаёт долю viewport, которая уже видна в prepared state (`0.28` по умолчанию — примерно первый ряд).
- `UiMaskedSlideReveal`: настраиваемый выезд одного `RectTransform` из-под host-authored mask и
  независимый fade второй `CanvasGroup`; optional timing-параметры позволяют собирать синхронные
  compound entrances, а `slideEase` переключает spring и простой smooth arrival; Reduced Motion
  сохраняет конечное состояние без translation.
- `UiCardCarouselMotion`: обычный content snap/drag режим и optional constant three-slot loop.
  В loop mode host передаёт ровно три view в порядке previous/current/next и authored center
  position. `PlayLoopStep(+1/-1)` уводит outgoing view вверх, сдвигает два соседних slots,
  вызывает host rebind пока recycled view за экраном и возвращает его сверху с другой стороны.
  Компонент блокирует повторный transition, восстанавливает resting positions при disable и
  применяет Reduced Motion fallback без создания/уничтожения карточек. `ClearLoopingSlots`
  возвращает компонент к обычному snap/drag режиму.
- `UiSelectiveGraphicDimming`: применяет color multiplier через `CanvasRenderer`, не меняет authored
  `Graphic.color`, не создаёт material instances и поддерживает исключённые подиерархии для ярких
  status marks поверх приглушённой карточки.
- `UiNavigationItem` / `UiTabMotion`, `UiCardSelectionMotion`, `UiListStagger`, `UiTooltipMotion`;
  tooltip использует тот же fade + scale pop contract, что и компактные popup-окна.

## Ambient и attention

- `UiIdleMotion`: один из breathing/bob/rotation/glow/shimmer/wiggle режимов, admission через shared budget; `ConfigureWiggle` предназначен для доступных drop/install targets и поддерживает паузу между циклами.
- `UiStarFieldMotion`: один lifecycle tween и один budget lease для массива authored `CanvasGroup`-звёзд;
  равномерно распределяет фазы мигания и восстанавливает authored alpha при disable/Reduced Motion.
- `UiAttentionPulse`: merge/queue по semantic key; включает короткий `Pulse`, длинный `SpringPulse`, entrance-эффект `BubbleReveal`; error имеет non-motion fallback.
- `UiShaderEffectGraphic`: ref-counted material variant, без material-per-frame.

## Values и celebration

- `UiNumberTicker`: integer/decimal/compact formatter, rapid-update merge, optional accessible completion feedback. TMP/host label подписывается на `DisplayTextChanged`.
- `UiProgressMotion`: main/ghost fill, decrease trail, sweep/milestone events, без layout-size animation.
- `UiLoadingScreenMotion`: циклически заполняет authored `UiSegmentedProgress` как indeterminate loader,
  по завершению доводит текущий проход до `1`, удерживает полный кадр и закрывается только через fade;
  завершение сообщает host через событие.
- `UiNotificationBadge`: count/dot, one-shot attention, opt-in urgent idle и runtime dot factory
  для UI, который собирается из существующей authored-иерархии; default urgent target scale — `1.25`.
- `UiTransientMessage`: повторяемый fade/scale pop для краткого validation/context feedback; host владеет
  локализованной строкой, а package — таймингом, lifecycle channel и восстановлением hidden baseline.
- `UiCurrencyFlyEffect`: pooled icons, captured endpoint, deterministic Bezier spread, quality limit.
- `UiCardTransferMotion`: двусторонний перелёт caller-owned card proxy по дуге с spring settle.
  Компонент не переподчиняет live layout items, может отслеживать движущийся destination во время
  ScrollRect drag и гарантированно отменяет tween при disable. Созданием/переиспользованием proxy
  и восстановлением ghosted presentation владеет host.
- `UiRewardSequence`: composable steps, minimum readable time, accelerate/skip и deterministic final state.

Temporary particles, trails и project-specific effects подключаются через hooks и должны использовать host/package pool; instantiate-per-burst не допускается.
