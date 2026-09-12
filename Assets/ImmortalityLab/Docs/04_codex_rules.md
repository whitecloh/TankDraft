# 04. Codex Rules — Immortality Lab

## 1. Роль

Ты работаешь в Unity-проекте `Assets/ImmortalityLab`.

Проект архитектурно чувствительный. Нельзя писать код как быстрый прототип. Любое изменение должно сохранять:

- runtime consistency;
- save/load consistency;
- scene-authored UI contract;
- death pipeline;
- craft pipeline;
- no offline progress rule.

## 2. Документы перед кодом

Перед изменениями прочитай минимум:

```text
Assets/ImmortalityLab/Docs/00_accepted_design_decisions.md
Assets/ImmortalityLab/Docs/02_game_design_document.md
Assets/ImmortalityLab/Docs/03_architecture_guide.md
Assets/ImmortalityLab/Docs/05_data_and_content_spec.md
```

Если задача меняет gameplay contract, сначала обнови документы, потом код.

## 3. Source of truth

Source of truth:

- Runtime/ECS state — активный gameplay.
- ScriptableObject — authored content.
- Save DTO — persistence.
- UI — только отображение и requests.

UI не должен:

- начислять EXP/Coins;
- вычислять craft output;
- убивать юнитов;
- создавать graves;
- выбирать death reward;
- симулировать offline progress;
- хранить gameplay truth.

## 4. Главный приоритет

Порядок приоритета:

1. Correct runtime flow.
2. Correct save/load.
3. Correct authored data contract.
4. UI sync.
5. Presentation/animation polish.

Не делай visual-first решения, которые подменяют runtime.

## 5. Death system rules

### 5.1 Every death grants EXP

Жёсткое правило:

```text
Любая смерть юнита должна выдавать Experience по DeathRewardRuleData.
```

Нельзя реализовывать старую модель:

```text
if death is new -> grant XP
else -> no XP
```

Правильно:

```text
ResolveDeathRewardRule(deathCause)
GrantExperience(reward.experiencePerDeath)
Then register discovery/repeat
```

### 5.2 Common death pipeline only

Все смерти идут через общий pipeline:

```text
UnitDeathDetected
-> ResolveDeathCause
-> ResolveDeathRewardRule
-> GrantExperience
-> RegisterDeathDiscoveryOrRepeat
-> RemoveFromAssignment
-> CreateGrave
-> Emit events
-> Save
```

Нельзя:

- удалять unit напрямую из View;
- создавать grave в Laboratory UI;
- начислять EXP в LocationPanelController;
- обходить death reward resolver;
- иметь отдельные death pipelines для lab, hazard и lifetime.

### 5.3 DeathDiscovery is not XP gate

DeathDiscovery нужен для journal, first-time marker, unlocks, stats и optional bonus.

Он не должен блокировать base EXP за повторную смерть.

## 6. Crafting rules

### 6.1 Unknown recipe outputs coal

Unknown recipe всегда выдаёт:

```text
item_coal
```

Нельзя:

- возвращать null;
- ничего не выдавать;
- создавать уникальный failed potion на каждый recipe key;
- хранить recipe metadata внутри inventory stack `item_coal`;
- позволять UI решать, какой output получится.

### 6.2 Exact recipe matching

Recipe key:

- order-insensitive by default;
- quantity-sensitive;
- exact-match only.

Если `snake + bottle` валиден, это не значит, что `snake + bottle + cookie` валиден. Последний даёт `item_coal`, если нет отдельного authored recipe.

### 6.3 Recipe history

Recipe history может хранить tried recipe key and output для preview:

```text
recipeKey -> outputId item_coal
```

Но это не делает coal уникальным предметом.

## 7. Offline progress forbidden

Не реализовывать offline progress.

Нельзя добавлять:

- offline simulation service;
- wall-clock catch-up;
- maxOfflineProgressSeconds;
- offline grave generation;
- offline shop refresh;
- offline worker gathering;
- offline deaths.

На load игра восстанавливает сохранённые timers и продолжает с них.

## 8. ECS / systems rules

### 8.1 Gameplay logic in systems/services

Gameplay logic живёт в systems, services и runtime models.

View classes только:

- отображают view data;
- отправляют requests;
- запускают presentation.

### 8.2 Requests and Events

Use:

- `Request` for commands;
- `Event` for completed facts.

Examples:

- `CraftRecipeRequest` -> `CraftCompletedEvent`.
- `RunExperimentRequest` -> `UnitDiedEvent` + `ExperienceGrantedEvent`.
- `BuyUpgradeRequest` -> `UpgradePurchasedEvent`.

### 8.3 Wiring required

Новая система не считается готовой, если она не подключена в composition root.

## 9. UI rules

### 9.1 Scene-authored UI only

UI собирается вручную в сценах/префабах.

Код:

- использует `SerializeField`;
- не создаёт основную UI hierarchy runtime-ом;
- не пишет runtime UI factory без явного запроса;
- не использует `GetComponent` во View вместо serialized references.

### 9.2 No defensive UI noise

Если обязательная ссылка не прокинута, это ошибка сборки сцены/префаба. Не добавляй большие validator layers и silent fallbacks без необходимости.

### 9.3 Popups are not gameplay state

Inventory, Recipe Book, Upgrade Tree, Death Journal и Naming popup не владеют состоянием. Они показывают view data и отправляют requests.

## 10. Save / Load rules

Save DTO stores IDs and serializable values only.

Do not save:

- MonoBehaviour refs;
- scene object refs;
- animation state;
- calculated view data;
- offline catch-up result.

Save must include data needed to resume:

- currencies;
- owned units;
- assignments;
- inventory;
- recipe history;
- potion custom names;
- death discoveries/repeat counts;
- graves;
- active timers;
- upgrades.

## 11. Data rules

ScriptableObject data should include:

- units;
- items;
- potions;
- craft recipes;
- death reward rules;
- death discoveries;
- locations;
- hazards;
- upgrades.

Never confuse:

- unit type and owned unit;
- item definition and inventory stack;
- potion definition and player custom name;
- death reward rule and death record;
- recipe output and recipe history.

## 12. Simplicity rules

Prefer:

- small targeted systems;
- simple data contracts;
- explicit IDs;
- deterministic recipe key generation;
- clear event flow.

Avoid:

- giant GameManager;
- broad refactors not needed for task;
- duplicate pipelines;
- speculative generic frameworks;
- UI-driven gameplay.

## 13. Final response format after coding

After task completion, report:

- what changed;
- what gameplay flow now works;
- what was verified;
- which `SerializeField` references must be wired in Inspector;
- any known limitations.
