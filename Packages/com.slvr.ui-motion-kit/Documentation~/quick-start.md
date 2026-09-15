# Quick start для существующего static UGUI window

1. Установите Free DOTween и выполните `Setup DOTween...`; затем добавьте package.
2. Выберите стабильный layout-root. Если его transform контролирует `LayoutGroup`, `ContentSizeFitter` или `AspectRatioFitter`, выполните `Tools > SLVR > UI Motion > Add VisualRoot to Selection`.
3. Добавьте один structural component (`UiPanelTransition`) и interaction-компоненты только интерактивным элементам.
4. Назначьте preset/theme. Начинайте с `Mobile_Balanced`; для плотного PC UI используйте `HMD_Subtle`.
5. Через inspector Preview проверьте press/focus/open/close и восстановите preview state.
6. В Play Mode проверьте touch release/cancel, mouse hover, keyboard/gamepad focus+submit.
7. Проверьте intensity `0/0.5/1`, Reduced Motion, Disable Flashes и скрытие/повторное открытие.
8. Запустите `Audit Selected Canvas`, исправляя findings осознанно.
9. Снимите Profiler/Frame Debugger baseline и after на одинаковом resolution/device.

Пакет не вызывает gameplay-команды и не владеет window/navigation lifecycle. Host bridge вызывает `Show/Hide`, обновляет logical visibility и связывает semantic events с существующими сервисами.
