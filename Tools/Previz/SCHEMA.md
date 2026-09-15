# Layout JSON schema v2

Schema v1 остаётся совместимой: она содержит viewport, theme и плоский массив elements. В schema v2 необязательный массив `groups` добавляет повторяемую UGUI-вёрстку, а `Element.parentId` создаёт hierarchy элементов. Корневые группы не вкладываются друг в друга; hierarchy элементов поддерживается.

```json
{
  "schemaVersion": 2,
  "screen": "arena",
  "state": "normal",
  "viewport": { "width": 576, "height": 1280, "safeTop": 0, "safeBottom": 0 },
  "theme": { "background": "#101820", "accent": "#d9a441", "text": "#ffffff" },
  "elements": [
    { "id": "gold", "kind": "panel", "label": "1200", "x": 20, "y": 24, "width": 120, "height": 48 },
    { "id": "nestedLabel", "kind": "label", "label": "new", "x": 30, "y": 34, "width": 40, "height": 16, "parentId": "gold" }
  ],
  "groups": [
    {
      "id": "wallet", "type": "horizontal", "alignment": "UpperLeft",
      "x": 20, "y": 24, "width": 120, "height": 48,
      "cellWidth": 120, "cellHeight": 48, "spacingX": 10, "spacingY": 0,
      "paddingLeft": 0, "paddingRight": 0, "paddingTop": 0, "paddingBottom": 0,
      "columns": 1, "children": ["gold"]
    }
  ]
}
```

Координаты каждого элемента `x`/`y` остаются глобальными координатами viewport даже при `parentId`. Для дочернего элемента importer вычисляет local coordinates относительно родителя. Прямой член группы не задаёт `parentId`; дочерние элементы такого члена разрешены.

`groups` содержит от 0 до 50 групп, `elements` — от 1 до 200 элементов. У группы `type` равен `horizontal`, `vertical` или `grid`; `alignment` — один из `UpperLeft`, `UpperCenter`, `UpperRight`, `MiddleLeft`, `MiddleCenter`, `MiddleRight`, `LowerLeft`, `LowerCenter`, `LowerRight`. `columns` — целое от 1 до 200; для grid фактическое число колонок равно `min(columns, children.length)`. Horizontal всегда использует одну строку, vertical — одну колонку, grid размещает членов row-major от upper-left.

У группы общие положительные `cellWidth`/`cellHeight`, неотрицательные `spacingX`/`spacingY`, четыре неотрицательных целых padding и список `children` от 1 до 200 ID. Все прямые члены одной группы имеют одинаковый `kind`. Элемент принадлежит максимум одной группе; ID группы не совпадает с ID элемента и содержит только латинские буквы, цифры, точку, дефис или подчёркивание. Importer отклоняет duplicate ownership, null/duplicate children, циклы `parentId`, выход content за доступную область и несовпадение заданной групповой геометрии с bounds её членов более чем на 0.05 px. Непустой `groups` в schema v1 отклоняется.

Полный пример главной с двумя группами: `Samples/arena-layout-v2.json`. Markdown export также содержит параметры групп; JSON остаётся источником для Unity importer.
