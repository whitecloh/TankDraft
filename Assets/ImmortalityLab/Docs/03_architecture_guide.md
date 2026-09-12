# 03. Architecture Guide — Immortality Lab

## 1. Назначение

Документ фиксирует архитектурный контракт Unity-проекта **Immortality Lab**. Его цель — не дать Codex написать быстрый прототип с `GameManager`, UI-логикой и несогласованным save/load.

## 2. Технологический baseline

- Unity.
- 2D, PC-first.
- Gameplay runtime: ECS-style systems, рекомендовано LeoECSLite по аналогии с прошлым проектом.
- UI: uGUI + Canvas.
- Animation/presentation: DOTween через общий animation manager.
- Authored content: ScriptableObject.
- Save: DTO, без ссылок на scene objects.

## 3. Source of truth

Authoritative state:

- Runtime/ECS state для активной игры.
- ScriptableObject assets для authored data.
- Save DTO только для persistence.
- UI view data только для отображения.

UI не является source of truth.

UI не должен:

- начислять Coins/EXP;
- решать crafting output;
- решать смерть юнита;
- создавать grave;
- выбирать death reward;
- симулировать offline progress;
- хранить gameplay state.

UI может:

- показывать prepared view data;
- отправлять requests;
- проигрывать presentation;
- показывать errors/block reasons, полученные из runtime.

## 4. Слои проекта

Рекомендуемая структура:

```text
Assets/ImmortalityLab/
  Docs/
  Scripts/
    Data/
    Models/
    Services/
    ECS/
      Components/
      Requests/
      Events/
      Systems/
      Installers/
    View/
      Bridges/
      Controllers/
      ItemViews/
      Animations/
```

### Data

ScriptableObject configs:

- units;
- items;
- potions;
- recipes;
- death reward rules;
- death discoveries;
- locations;
- hazards;
- upgrades;
- global settings.

### Models

- runtime models;
- save DTO;
- view data;
- recipe keys;
- death cause/reward result;
- location runtime state.

### Services

- config lookup;
- save/load;
- recipe resolver;
- death reward resolver;
- inventory service;
- RNG service;
- time/tick helpers.

### ECS

- components;
- requests;
- events;
- systems;
- composition root.

### View

- scene-authored screen controllers;
- panel controllers;
- item views;
- bridges sending requests and receiving view data;
- animation manager.

## 5. Phase model

Suggested phases:

- `Boot`;
- `MainLoop`;
- `PopupOpen` presentation-only;
- `Ending`.

Because the game is one-screen incremental, most systems can run in `MainLoop` if their feature is unlocked and runtime state is valid. Still, each system must have guards:

- Shop systems run only if shop unlocked.
- Graveyard systems run only if graveyard unlocked.
- Craft systems process only craft requests.
- Lab systems process only lab requests.
- Location systems tick only active assignments.
- Ending systems run only after `immortality_final`.

## 6. Requests and events

Use consistently:

- Request = command to perform action.
- Event = action already happened.

Examples:

### Requests

- `BuyUnitRequest`
- `RefreshShopRequest`
- `AssignUnitToLocationRequest`
- `UnassignUnitRequest`
- `StartLocationActionRequest`
- `ClaimLocationDropRequest`
- `CraftRecipeRequest`
- `PlaceUnitInLabRequest`
- `RunExperimentRequest`
- `CollectGravePickupRequest`
- `BuyUpgradeRequest`
- `StartEndingRequest`

### Events

- `UnitBoughtEvent`
- `LocationActionCompletedEvent`
- `ItemAddedEvent`
- `CraftCompletedEvent`
- `UnknownRecipeCraftedEvent`
- `UnitDiedEvent`
- `ExperienceGrantedEvent`
- `DeathDiscoveryUnlockedEvent`
- `GraveCreatedEvent`
- `GravePickupCollectedEvent`
- `UpgradePurchasedEvent`
- `LocationUnlockedEvent`
- `EndingUnlockedEvent`

## 7. Critical runtime contracts

### 7.1 Death pipeline

Единственный разрешённый путь смерти:

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

Rules:

- `GrantExperience` выполняется для каждой смерти.
- Repeat death не блокирует base EXP.
- DeathDiscovery is optional journal state, not XP gate.
- Grave creation must happen for every unit death unless explicitly excluded by authored special rule.
- UI cannot kill or remove units directly.

### 7.2 Death reward resolver

`DeathRewardResolver` принимает `DeathCause` и выбирает `DeathRewardRuleData`.

Matching priority:

1. Potion or DeathEffect exact rule.
2. Hazard exact rule.
3. DamageType rule.
4. Lifetime rule.
5. Fallback rule.

Output:

```text
DeathRewardResult
- experienceGranted
- firstDiscoveryBonusGranted
- coinsGranted
- itemDrops
- graveIncomeMultiplier
- discoveryId optional
```

### 7.3 Craft pipeline

```text
CraftRecipeRequest
-> Validate ingredients
-> Build normalized RecipeKey
-> RecipeResolver.Resolve(recipeKey)
-> Consume ingredients
-> Add authored output OR item_coal
-> Save recipe history
-> Emit CraftCompletedEvent
```

Rules:

- recipe matching is exact;
- order-insensitive by default;
- unknown recipe always outputs `item_coal`;
- inventory `item_coal` does not store recipe metadata;
- recipe history may store tried keys for preview;
- UI cannot resolve recipe itself.

### 7.4 Location action pipeline

```text
StartAction
-> Check cost/resources/worker
-> Begin timer
-> Tick timer
-> Roll drop table
-> Create location drop
-> Manual or auto claim
-> Add item to inventory
```

Hazards tick separately on assigned units and call common death pipeline when a unit dies.

### 7.5 Cemetery pipeline

```text
Grave timer tick
-> Create pickup if storage not full
-> Player/collector claims pickup
-> Grant Coins
-> Save
```

Graveyard does not own death logic. It only receives grave creation through death pipeline.

### 7.6 No offline progress

Save/load must not calculate progress based on wall-clock time while the app was closed.

Allowed:

- store remaining timers;
- store playtime;
- resume timers after load.

Not allowed:

- catch-up grave income;
- catch-up shop refresh;
- catch-up gathering;
- offline unit deaths;
- `maxOfflineProgressSeconds`.

## 8. Data rules

### 8.1 Runtime entity != type config

Strictly separate:

- `UnitTypeData` vs owned unit instance;
- `ItemData` vs inventory stack;
- `PotionData` vs player custom potion name;
- `LocationData` vs location runtime state;
- `DeathRewardRuleData` vs granted reward event;
- `DeathDiscoveryData` vs player discovery state.

### 8.2 Visual data

Authored data stores:

- sprites;
- icons;
- animation IDs;
- VFX IDs;
- colors;
- localization keys.

It should not store gameplay prefabs with logic.

## 9. Save / Load policy

Save DTO stores:

- currencies;
- owned units;
- inventory;
- recipe history;
- potion custom names/descriptions;
- death discoveries and repeat counts;
- death stats;
- graves;
- location state;
- assignments;
- active timers;
- shop state;
- upgrade state;
- ending state.

Save DTO does not store:

- scene object references;
- MonoBehaviour refs;
- animation state;
- popup state as truth;
- calculated view data;
- offline progress results.

Broken save policy:

- If a save is structurally broken, do not continue inconsistent runtime.
- Reset only the unsafe runtime portion if possible.
- Prefer safe fresh state over half-restored state.

## 10. UI architecture

### 10.1 Scene-authored UI

UI is assembled manually in scenes/prefabs.

Code should:

- expose `SerializeField` references;
- not create main UI hierarchy at runtime;
- not use runtime UI factories as default;
- not use `GetComponent` in View if a serialized reference is expected.

### 10.2 Window/popup rules

Main screen is always present. Popups are overlays.

Popup examples:

- Inventory;
- Recipe Book;
- Potion Naming;
- Unit Picker;
- Upgrade Tree;
- Death Journal;
- Ending Stats.

Popup does not own gameplay state.

### 10.3 View data

Runtime produces view data. UI consumes it.

Examples:

- `ShopViewData`;
- `GraveyardViewData`;
- `CraftViewData`;
- `LabViewData`;
- `LocationViewData`;
- `UpgradeTreeViewData`;
- `DeathJournalViewData`.

## 11. Animation policy

All UI/presentation animation should go through `UiAnimationManager` or a small feature-specific presentation service approved by architecture.

Animations must not:

- decide gameplay outcome;
- block runtime invisibly;
- become source of truth;
- hide missing mandatory references.

## 12. Composition root

Any new system is incomplete until it is wired in `EcsCompositionRoot` / equivalent installer.

Any new View bridge is incomplete until it is wired in `UiCompositionRoot` / scene object.

## 13. Pre-code checklist

Before implementing a feature, answer:

1. Which layer owns the logic?
2. What is the authoritative state?
3. Which request/event is used?
4. Does it affect save/load?
5. Does it affect death pipeline?
6. Does it affect craft pipeline?
7. Does it require new authored data?
8. Does it violate no-offline-progress?
9. Does UI only display and send requests?
