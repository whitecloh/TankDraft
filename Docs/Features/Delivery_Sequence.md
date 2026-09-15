# Последовательность работ
Status: historical bootstrap roadmap; current execution order is Functional_Release_Plan
Last reviewed: 2026-09-13

## Текущий статус и порядок

Главный порядок выполнения — [Functional_Release_Plan.md](Functional_Release_Plan.md): сначала первый удалённый PvP, затем устойчивость/защита, серверный профиль, полные боевые механики, мета, монетизация, мобильный флоу и эксплуатационная приёмка. Арт, VFX и визуальная полировка в этот порядок не входят.

Локальный матч, очередь, reconnect и access renewal подтверждены на PC и Android USB в LocalOnly QA. Public hosting, production identity, distributed persistence/lease и remote PvP ещё не выполнены; локальные прогоны не заменяют их. Технические S1–S7 и их evidence остаются в [Backend_Implementation_Plan.md](Backend_Implementation_Plan.md).

## Исторический bootstrap overview

| Этап | Выход / критерий |
|---|---|
| 1. Документы и Codex | Единые требования TankDraft, короткие правила, маршрутизация, прежний шаблон явно superseded |
| 2. Unity MCP | Локальный endpoint нужного проекта; прямые state/scene/Console проверки |
| 3. Пакеты и setup | Закреплённые необходимые версии, resolve/компиляция, без Odin и чужих IDs |
| 4. Архитектура и экраны | ECS/data/presentation/network boundaries, ряды, UI/state map и открытые балансовые решения |
| 5. Превиз | Рабочий редактор/набор состояний, экспорт и проверка в Unity |
| 6. Сквозной матч | Колода → драфт/камбэк → раунды → итог/мета, две стороны и бот |
| 7. Полный продукт | Все наблюдаемые системы, реальный PvP, боты, IAP/Ads/пропуск/рейтинг и mobile QA |

Полная мета — существенная работа: серверная экономика, валидация покупок, восстановление прогресса и сетевые отказы добавляются к экранной вёрстке. Объём принят, сроки не оценены без декомпозиции.
SDK installation ≠ online integration; превиз ≠ сверстанный продукт; локальный матч ≠ релиз.

Эти этапы описывают стартовую последовательность 12.09.2026. Их результаты были расширены локальным боевым/очередным прототипом; текущую приоритизацию и границы доказательств задаёт [Functional_Release_Plan.md](Functional_Release_Plan.md).

