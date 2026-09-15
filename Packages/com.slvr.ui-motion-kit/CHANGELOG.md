# Changelog

## [Unreleased]

- Disabled-состояние `UiAnimatedButton` больше не уменьшает authored alpha: input по-прежнему
  блокируется через `Button.interactable`, но кнопка не становится бесцветной.
- Добавлен `UiGraphicColorFlash`: повторяемый validation-feedback для UGUI Graphic с плавным
  переходом в заданный цвет и гарантированным восстановлением актуального исходного цвета.
- Добавлены `UiLoadingScreenMotion` для циклического indeterminate segmented loader с завершением
  текущего прохода перед fade и `UiStarFieldMotion` для общего budgeted мигания массива authored звёзд.
- `UiNumberTicker` теперь напрямую обновляет TMP label; `UiStagedReveal` получил
  последовательный TMP typewriter step, а новый `UiCalendarTextRoller` выполняет lifecycle-safe
  roll-up/roll-in для короткого календарного значения без обновлений в `Update`.

- `UiCardCarouselMotion` получил constant three-slot loop mode: previous/current/next views
  переиспользуются без создания view на каждый логический элемент.
- При шаге вперёд/назад outgoing-card уходит вверх, соседние slots синхронно сдвигаются,
  затем recycled view перебиндится за экраном и возвращается сверху с противоположной стороны.
- Loop mode блокирует reentrant transition, восстанавливает authored positions при disable и
  соблюдает global Translation/Reduced Motion policy.

## [0.10.6] - 2026-07-24

- Добавлен `UiTransientMessage`: lifecycle-safe временное сообщение с fade/scale pop,
  локализационно-нейтральное и пригодное для validation feedback.
- Default urgent pulse нотификаторов усилен с `1.04` до `1.25`; authored integrations должны
  мигрировать сериализованное значение, чтобы не перекрывать новый default.

## [0.10.5] - 2026-07-23

- `UiMaskedSlideReveal` получил настраиваемый `slideEase`; default `OutBack` сохраняет обратную
  совместимость, а спокойные составные выезды могут использовать `OutCubic`.

## [0.10.4] - 2026-07-23

- Добавлен `UiSelectiveGraphicDimming`: renderer-level tint для выбранных UGUI-элементов без
  material instances и без изменения authored colors.
- Подиерархии можно исключить из затемнения, поэтому status marks и другие смысловые акценты
  сохраняют исходную яркость.
- `UiMaskedSlideReveal.Configure` получил необязательные timing-параметры для host-authored
  составных выездов без дублирования motion-компонента.

## [0.10.3] - 2026-07-23

- Добавлен reusable `UiCardTransferMotion`: одинаковый forward/reverse тайминг, quadratic arc,
  scale overshoot/settle и Reduced Motion fallback.
- Destination может отслеживаться во время полёта, поэтому ScrollRect остаётся интерактивным и
  обратный перелёт приходит в актуальную позицию карточки.
- Компонент анимирует caller-owned proxy, не переподчиняет layout items и отменяется через общий
  lifecycle channel при disable/destroy.

## [0.10.2] - 2026-07-23

- `UiNotificationBadge.CreateRuntimeDot` больше не обращается к удалённому в Unity 6 built-in sprite `UI/Skin/Knob.psd`.
- Fallback-dot использует стандартную белую UGUI texture; production-интеграциям по-прежнему рекомендуется authored badge prefab.

## [0.10.1] - 2026-07-23

- `UiPanelTransition.RewardPop` поддерживает глубокий стартовый scale вплоть до `0.05`.
- Entrance стал трёхфазным spring: быстрый expand до `1.08`, короткий возврат до `0.975`, затем settle в authored scale.
- Для RewardPop установлен минимальный motion-budget `0.52 s`; Reduced Motion сохраняет обычный короткий fade без spring.

## [0.10.0] - 2026-07-23

- `UiPanelTransition.PrepareShow` убирает one-frame flash перед popup entrance; runtime-конфигурация отделяет неподвижный backdrop от масштабируемого visual root.
- Добавлен reusable `UiStagedReveal`: последовательность равных fade/scale шагов с spring overshoot и Reduced Motion fallback.
- Добавлен TMP-based `UiTypewriterText`: строка назначается один раз, а reveal идёт через `maxVisibleCharacters` без substring-аллокаций и скачков layout.
- `UiTooltipMotion` получил тот же fade + scale pop contract, что и popup-окна.
- `UiNotificationBadge.CreateRuntimeDot` создаёт лёгкий reusable dot для runtime-composed UI.

## [0.9.6] - 2026-07-23

- Soft `RectMask2D` reveal получил `revealInitialVisibleFraction`: `0` полностью скрывает viewport в prepared state, `1` оставляет его видимым целиком.
- Значение по умолчанию `0.28` оставляет видимым примерно первый ряд сетки и мягкую границу следующего ряда, как в production-референсе.
- `ConfigureReveal` расширен обратно совместимым optional-параметром; migration существующих вызовов не требуется.

## [0.9.5] - 2026-07-23

- `UiScrollRectMotion` получил reusable top-to-bottom reveal через анимацию `RectMask2D.padding`.
- Мягкая граница использует authored `RectMask2D.softness` либо заданный минимальный vertical softness; размеры viewport/content и layout не меняются.
- `PrepareReveal`, `PlayReveal` и `ResetRevealImmediate` используют отдельный lifecycle channel, восстанавливают исходные padding/softness при complete/disable и отключаются через Reduced Motion policy.
- API расширен обратно совместимо; существующим интеграциям migration не требуется.

## [0.9.4] - 2026-07-22

- `UiScrollRectMotion.CenterOn` добавляет lifecycle-safe центрирование дочернего элемента внутри общего `ScrollRect`.
- Центрирование учитывает фактические bounds content/viewport, останавливает инерцию, ограничивает normalized position и соблюдает Reduced Motion.

## [0.9.3] - 2026-07-22

- `UiIdleMotion.ConfigureWiggle` поддерживает настраиваемую паузу между циклами без component `Update`.
- Wiggle-цикл остаётся в authored rotation во время паузы и продолжает учитывать общий lifecycle, motion budget и Reduced Motion.

## [0.9.2] - 2026-07-22

- `UiIdleMotion` получил reusable режим `Wiggle` для доступных drop/install targets.
- `Wiggle` создаёт короткую повторяющуюся дрожь через небольшой поворот, использует deterministic phase, общий motion budget и не добавляет component `Update`.
- Rotation рассчитывается как signed sine-offset поверх authored quaternion, поэтому переход через Unity `0/360°` не может превратиться в полный оборот.
- Reduced Motion и отключённые idle effects полностью останавливают wiggle и восстанавливают authored transform.

## [0.9.1] - 2026-07-22

- `UiAttentionPulse` получил переиспользуемые эффекты `SpringPulse` и `BubbleReveal`.
- `SpringPulse` использует более длинное многосоставное пружинное затухание для подтверждения клика.
- `BubbleReveal` начинает scale ниже authored state, проходит overshoot/undershoot и возвращается к исходному масштабу.
- Оба эффекта учитывают intensity, Reduced Motion и общий lifecycle/attention budget без component `Update`.

## [0.9.0] - 2026-07-22

- Добавлен reusable `UiScrollRectMotion`: мгновенный или анимированный возврат `ScrollRect` к настроенной normalized-позиции без `Update`.
- Добавлен `UiMaskedSlideReveal` для составного entrance: visual выезжает из-под родительской маски, независимая текстовая группа появляется через fade.
- Оба компонента используют общий lifecycle/settings/Reduced Motion контракт и покрыты EditMode-тестами.

## [0.8.2] - 2026-07-22

- `UiHoverLiftMotion` получил reusable режимы `LiftAndParallax` и `ScaleWave`.
- `ScaleWave` оставляет pointer hit-area неподвижной, удерживает authored position/rotation и создаёт один scale-loop только на время hover/focus.
- Выход из волны продолжает tween от фактически отображаемого scale без snap; Reduced Motion и нулевая intensity возвращают authored state.
- В easing vocabulary добавлен обратно совместимый `UiMotionEase.InOutSine`; добавлены EditMode и PlayMode regression tests.

## [0.8.1] - 2026-07-22

- `UiHoverLiftMotion` перенесён в reusable runtime package и доступен карточкам любых host-проектов.
- Pointer classification поддерживает и legacy `StandaloneInputModule`, и положительные mouse/pen device ids из `InputSystemUIInputModule`, не добавляя обязательную зависимость от `com.unity.inputsystem`.
- Та же классификация применена к `UiAnimatedButton` и `UiTooltipMotion`; touch остаётся без обязательного hover.
- Добавлены PlayMode-регрессии для legacy mouse, Input System-style positive mouse id и touch id.
- Публичный API расширен обратно совместимо; host-компоненты с собственной копией hover следует мигрировать на `SLVR.UIMotion.UiHoverLiftMotion`.

## [0.8.0] - 2026-07-21

- Добавлены `UiNumberTicker`, `UiProgressMotion`, общий component pool, pooled `UiCurrencyFlyEffect` и composable `UiRewardSequence`.
- Rapid updates сходятся к последнему значению; reward skip приводит к единому final state, Reduced Motion исключает крупный scale/rotation.
- Добавлены host-neutral showcase, semantic audio и haptics contracts; Spine вынесен в отдельный optional sample assembly.
- Добавлены report-first UI audit, генератор 15 sample-prefab’ов через Unity Editor API и standalone UI Motion Gallery.
- Package samples не содержат project art, fonts, audio clips или haptics SDK.
- Android burst profiling и clean-import verification остаются отдельными ручными gates и не объявлены пройденными без измерений.

## [0.7.0] - 2026-07-21

- Добавлен `UiNotificationBadge`: count/dot mode, one-shot zero-to-positive/increase entrance,
  merge rapid updates и explicit visibility bridge.
- Optional urgent pulse выключен по умолчанию, использует shared Accent budget и освобождает его при hide/disable.
- Reduced Motion заменяет scale entrance на fade; Low quality запрещает urgent idle pulse.
- Count label поддерживает UGUI `Text` напрямую и TMP/host adapters через `DisplayTextChanged`.
- Добавлены material lifecycle, per-frame variant stability и mask/RectMask2D pixel smoke tests.
- Добавлен локальный Gate 3 profiling report; Android low-quality GC/overdraw capture ожидает подключённое устройство.
- Публичный API расширен обратно совместимо; migration-действий не требуется.

## [0.6.0] - 2026-07-21

- Добавлены lightweight UGUI shaders `SLVR/UI/Shimmer`, `SLVR/UI/GradientFlow` и
  `SLVR/UI/SoftGlowOverlay` без GrabPass и screen-space blur.
- Все шейдеры поддерживают sprite atlas alpha, стандартные UGUI stencil properties,
  `UNITY_UI_CLIP_RECT` и `UNITY_UI_ALPHACLIP`.
- Добавлены build-retained template materials и ref-counted `UiShaderMaterialCache`.
- Добавлен `UiShaderEffectGraphic`, который назначает shared variant на время active lifecycle и
  восстанавливает исходный material при disable/destroy.
- Low quality, Reduced Motion и Disable Flashes отключают ambient shader phase animation.
- Задокументированы batching, mask depth, draw-call и overdraw tradeoffs.
- Публичный API расширен обратно совместимо; migration-действий не требуется.

## [0.5.0] - 2026-07-21

- Добавлен `UiAttentionPulse` с эффектами nudge, pulse, error shake, bounce и highlight ring.
- Реализована очередь по scope: одновременно работает один attention-эффект, разные semantic key
  выполняются последовательно, повторные запросы с одинаковым key объединяются.
- Для Reduced Motion и ошибок добавлены доступные color/outline/message fallback без обязательного shake.
- Добавлены PlayMode-регрессии для всех эффектов, queue/merge, lifecycle и error fallback.
- Публичный API расширен обратно совместимо; migration-действий не требуется.

## [0.4.0] - 2026-07-21

- Добавлен `UiIdleMotion` с breathing, bob, rotation, glow и shimmer-trigger режимами.
- Добавлен общий idle admission budget и nested modal suppression runtime.
- Idle loops создаются без component `Update`, получают deterministic phase delay и освобождают
  budget при hide/disable/modal suppression.
- Runtime settings provider применяет intensity, quality, Reduced Motion и idle-effects policy
  при каждом restart.
- `UiModalBackdrop` автоматически подавляет фоновые idle effects на время модального scope.
- Публичный API расширен обратно совместимо; migration действий не требуется.

## [0.3.0] - 2026-07-21

- Завершён Phase 2 interaction/structural motion.
- `UiAnimatedButton` получил semantic states/events, focus ring и optional visual hooks.
- Добавлены panel presets: bottom sheet, drawers, tooltip и reward pop.
- VisualRoot migration сохраняет RectTransform layout, sibling order и serialized references.
- Editor Preview дополнен Hover и Focus; расширены EditMode/PlayMode regression tests.
- Публичный API расширен обратно совместимо; migration действий не требуется.

## [0.2.0] - 2026-07-21

- Добавлены modal backdrop и screen content transitions.
- Добавлены navigation/tab, card selection, list stagger и tooltip motion components.
- Добавлены setup menus и editor preview на `DOTweenEditorPreview`.
- Публичный API расширен обратно совместимо; migration действий не требуется.

## [0.1.0] - 2026-07-21

- Создан embedded UPM package.
- Добавлены settings/theme/budget contracts.
- Добавлен DOTween policy layer с lifecycle и semantic channels.
- Добавлены базовые EditMode/PlayMode tests.
