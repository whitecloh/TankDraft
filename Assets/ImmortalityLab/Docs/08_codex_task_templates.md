# 08. Codex Task Templates — Immortality Lab

Use these prompts when assigning implementation tasks to Codex.

## 1. General task header

```text
Ты работаешь в Unity-проекте Assets/ImmortalityLab.
Перед кодом прочитай:
- Assets/ImmortalityLab/Docs/00_accepted_design_decisions.md
- Assets/ImmortalityLab/Docs/03_architecture_guide.md
- Assets/ImmortalityLab/Docs/04_codex_rules.md
- Assets/ImmortalityLab/Docs/05_data_and_content_spec.md

Сохраняй правила:
- UI не source of truth.
- Gameplay logic в systems/services/runtime models.
- Authored data в ScriptableObject.
- Любая смерть даёт EXP по DeathRewardRuleData.
- Unknown craft recipe даёт item_coal.
- Offline progress не реализовывать.
- UI scene-authored, не создавай runtime UI hierarchy.
```

## 2. Implement currencies and inventory

```text
Реализуй runtime currencies и inventory для Immortality Lab.
Нужны Coins, Experience, InventoryStackState, add/remove item requests, save/load DTO, view data для top bar/inventory.
UI должен только отображать view data и отправлять requests.
```

Acceptance criteria:

- Coins and Experience can be changed by runtime requests.
- Inventory supports add/remove/check.
- Save/load restores currencies and inventory.
- No UI class owns currency/inventory truth.

## 3. Implement shop and owned units

```text
Реализуй Shop и owned units.
Нужны UnitTypeData, OwnedUnitState, shop offer generation, BuyUnitRequest, UnitBoughtEvent, save/load, view data для shop/roster.
```

Acceptance criteria:

- Shop offers generated from authored unit types.
- Buying validates Coins in runtime.
- Bought unit has unique runtime ID.
- Save/load restores owned units and shop state.

## 4. Implement graveyard

```text
Реализуй Graveyard.
Каждая grave создаёт coin pickup по таймеру. Игрок может собрать pickup вручную. Позже caretaker сможет auto collect.
Не реализовывай offline progress: после load timers продолжаются с сохранённых значений.
```

Acceptance criteria:

- Grave creates pickup after timer.
- Collecting pickup grants Coins.
- Save/load restores timer and stored pickups.
- No catch-up Coins generated while game was closed.

## 5. Implement first gathering location

```text
Реализуй первую безопасную gathering location.
Игрок кликает action, ждёт duration, получает item drop, claim добавляет item в inventory.
Worker slot может повторять action автоматически.
```

Acceptance criteria:

- Manual action creates drop.
- Claim adds item.
- Worker automation works while game is running.
- Save/load restores active action timer.
- No offline gathering catch-up.

## 6. Implement crafting with coal fallback

```text
Реализуй crafting по GDD v2.
Нужны ItemData, PotionData, CraftRecipeData, RecipeKeyBuilder, RecipeResolver, CraftRecipeRequest, CraftCompletedEvent, RecipeHistoryState.
Правило: exact authored recipe -> configured output. Any non-authored exact combination -> item_coal.
item_coal универсальный и не хранит recipe metadata в inventory stack.
```

Acceptance criteria:

- snake + bottle -> poison if authored.
- snake + cookie -> item_coal if not authored.
- snake + bottle + cookie -> item_coal unless exact recipe exists.
- Inputs are consumed.
- Output is added to inventory.
- Recipe history stores tried recipe -> output for preview.
- Inventory coal stacks are generic.

## 7. Implement laboratory and common death pipeline

```text
Реализуй Laboratory и common death pipeline.
Нужны PlaceUnitInLabRequest, RunExperimentRequest, DeathCause, DeathEffectData, DeathRewardRuleData, DeathRewardResolver, UnitDiedEvent, ExperienceGrantedEvent, GraveCreatedEvent.
Главное правило: любая смерть выдаёт EXP по DeathRewardRuleData. Повторная смерть тем же potion тоже выдаёт configured base EXP.
DeathDiscovery — журнал/first-time marker, не gate для base EXP.
```

Acceptance criteria:

- Unit in lab slot does not lose HP/lifetime passively.
- Experiment consumes potion.
- Unit dies through common death pipeline.
- Potion death grants configured EXP.
- Repeating same potion death grants configured EXP again.
- Grave is created.
- Death discovery first-time state is saved separately.

## 8. Implement lifetime death

```text
Добавь lifetime tick and lifetime death.
Если LifetimeSecondsRemaining <= 0, юнит умирает через common death pipeline.
Lifetime death grants EXP by DeathRewardRuleData, e.g. 1 EXP.
No offline lifetime reduction.
```

Acceptance criteria:

- Lifetime decreases only while game runs and unit is not protected by lab slot.
- Lifetime <= 0 triggers death once.
- EXP is granted.
- Grave is created.
- Save/load does not apply offline lifetime delta.

## 9. Implement hazardous location

```text
Добавь hazardous location, например Radioactive Zone.
Hazard наносит typed damage by cooldown, defenses reduce damage, location can reduce lifetime by configured interval.
Any death uses common death pipeline and grants EXP by DeathRewardRuleData.
```

Acceptance criteria:

- Damage tick works.
- Defense percent reduces damage.
- Lifetime influence works.
- Hazard death grants configured EXP.
- Death creates grave.
- No duplicate death event.

## 10. Implement casino

```text
Добавь Casino location.
3 slot machines. Each spin costs Coins, plays duration/animation, rolls item drop. Worker can auto spin but every spin still costs Coins. Collector can auto claim drops.
Important: Casino has no periodic damage and no lifetime influence.
```

Acceptance criteria:

- Manual spin checks and spends Coins.
- Not enough Coins blocks spin.
- Spin creates item drop.
- Worker auto spin spends Coins each time.
- Collector claims drops.
- No hazard/lifetime tick runs in Casino.

## 11. Implement upgrade tree

```text
Реализуй upgrade tree.
Upgrades cost Experience/Coins/items and apply runtime effects.
Include early upgrades: graveyard auto collect, gathering speed, lab notebook, thrift hiring, coal recycling.
```

Acceptance criteria:

- Purchase validates costs in runtime.
- EXP is spent.
- Upgrade effect changes runtime behavior.
- Save/load restores purchased upgrades.
- UI only displays node view data and sends BuyUpgradeRequest.

## 12. Implement death journal and recipe book

```text
Реализуй Death Journal и Recipe Book view data.
Death Journal показывает discoveries, repeat counts and total EXP from death categories.
Recipe Book показывает tried recipes, including unknown recipe -> item_coal.
```

Acceptance criteria:

- Repeat deaths update repeat counts.
- Journal does not imply only first death gives EXP.
- Recipe book previews coal for previously tried unknown recipes.
- Data comes from runtime/save.

## 13. Implement ending

```text
Реализуй final immortality flow.
Final upgrade unlocks immortality state. Main scene shows ending clickable object. Click sends StartEndingRequest. Ending sequence opens scene-authored popup with stats and credits.
Stats: playtime, total deaths, total EXP from deaths, unique discoveries, repeat deaths, coal created, potions crafted, units bought, graves created.
```

Acceptance criteria:

- Final node requires prerequisites.
- Click triggers ending once.
- Stats are from runtime/save state, not UI counters.
- Save/load after ending is stable.

## 14. Template for docs-first change

```text
Измени документы перед кодом.
Product change: [описание].
Обнови минимум:
- 00_accepted_design_decisions.md, если меняется принятое правило;
- 02_game_design_document.md, если меняется gameplay;
- 03_architecture_guide.md, если меняется runtime/save/UI contract;
- 05_data_and_content_spec.md, если меняются data structures;
- 06_implementation_roadmap.md, если меняется порядок работ.
После этого перечисли изменённые sections и причину.
```
