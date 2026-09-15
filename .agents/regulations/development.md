# Разработка
Status: active
Last reviewed: 2026-09-12

Перед C# правкой проверять owning asmdef, входы, lifecycle, зависимости и способ проверки.
Боевой домен — LeoECS Lite и C# данные; Unity, PUN, UI, persistence SDK не должны проникать в компоненты/системы симуляции.
ECS не заменяет транспорт, сохранения и authoring. Не сериализовать raw entity index как долговечный ID.
ScriptableObject/каталог — исходные настройки; runtime получает копию/снапшот. Не мутировать authored asset в бою.
Числа баланса, строки, ссылки на prefab, визуальные параметры — конфиги. Алгоритмы, проверки и переходы — код.
GameObjects UI/юнитов/VFX создаются из подготовленных prefabs через pool/spawn service. Не собирать production UI посредством new GameObject/AddComponent. Создание/редактирование иерархии в editor-инструменте допустимо, результат — сохраняемый prefab.
Повторяющиеся UI-элементы одинакового назначения берутся из общего authored prefab и размещаются `HorizontalLayoutGroup`, `VerticalLayoutGroup` или `GridLayoutGroup` по структуре: единые размеры задаются `LayoutElement` либо `cellSize`, шаг — `spacing`/`padding`, центровка — `childAlignment`. Не задавать вручную размеры и позицию каждому sibling; authored исключения фиксировать явно. LayoutGroup управляет слотами, а визуальные эффекты и анимация остаются во внутреннем child.
Без Odin: SerializedObject/SerializedProperty, обычные custom inspectors/EditorWindow, OnValidate и явные валидаторы.
В hot path избегать LINQ/строк/Find и лишних аллокаций; подписки освобождать, UniTask связывать с lifetime.
Глобальные service locator/параллельные event bus и второй менеджер UI-анимаций не вводить.

Папки собственного кода и ассетов: Docs/Features/Folder_Architecture.md. Runtime раскладывать по feature и слою; Configs/Prefabs/Art — по назначению. Переносы через AssetDatabase.MoveAsset с сохранением GUID и обновлением authoring/validators/Tools в том же патче.
