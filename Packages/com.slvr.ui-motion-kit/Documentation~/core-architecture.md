# Core architecture

- `UiMotionTweenFactory` применяет `SetTarget`, `SetLink`, explicit update mode, ease и recycling.
- `UiMotionLifecycle` владеет `UiMotionChannelSet` и реагирует на disable/destroy.
- В одном semantic channel активен не более чем один tween.
- Interaction/one-shot tweens уничтожаются при disable; idle tweens могут pause/resume.
- Global DOTween cleanup запрещён, чтобы не затрагивать tweens host project.

## Package и host boundary

- Универсальный effect/interaction без зависимости от host data и window framework живёт в package.
- Host adapters могут связывать package-компоненты с Foundation/другим window framework, но не создают отдельную motion policy.
- Screen-specific choreography может временно оставаться в host во время polishing; перед package release проходит promotion review и выделяет host-neutral recipe.
- Новая reusable-возможность считается зафиксированной только вместе с test coverage, component docs и changelog entry.
