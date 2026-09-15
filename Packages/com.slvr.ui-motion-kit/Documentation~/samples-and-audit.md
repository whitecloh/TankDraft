# Samples, gallery и UI audit

В Package Manager импортируйте `UI Motion Gallery`. Sample содержит 15 prefab’ов и отдельную сцену, не добавленную в production build settings. Placeholder-графика использует встроенные UGUI primitives и не содержит project fonts/art/audio.

Gallery включает sections для buttons, tabs, modals, tooltips, cards, progress, counters, badges, idle, currency, rewards, quality, Reduced Motion и debug stats. Runtime controls переключают theme/quality/intensity/accessibility provider.

Source samples пересобираются командой `Tools > SLVR > UI Motion > Generate Package Samples`. Генератор использует `PrefabUtility` и `EditorSceneManager`; prefab/scene YAML вручную не редактируется.

Audit-команды:

- `Audit Selected Canvas`;
- `Audit All UI In Open Scenes`.

Проверяются Animator misuse, touch targets, decorative raycasts, GraphicRaycaster, large/deep UI, layout-controlled motion, overdraw layers, idle budget, material variants, hidden effects и focus/navigation. Audit report-only. Единственный safe fix — явный opt-in для доказанно decorative raycast targets с confirmation и Undo.
