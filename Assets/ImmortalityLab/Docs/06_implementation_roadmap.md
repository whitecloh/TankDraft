# 06. Implementation Roadmap — Immortality Lab

## 1. Goal of first playable

Первый playable должен доказать полный loop:

```text
buy unit
-> gather item
-> craft potion or coal
-> death happens
-> every death grants EXP by config
-> grave appears
-> grave produces Coins
-> upgrade is bought
-> save/load restores all core state
```

## 2. Stage 0 — Project skeleton

### Goal

Создать базовую структуру Unity-проекта и docs-first контракт.

### Implement

- folders under `Assets/ImmortalityLab`;
- composition root;
- config registry service;
- save service stub;
- runtime world/state;
- basic tick system;
- scene-authored main screen shell.

### Acceptance criteria

- Project compiles.
- Docs are in `Assets/ImmortalityLab/Docs`.
- Runtime can start new save.
- UI shell displays empty top bar values from runtime view data.

## 3. Stage 1 — Currencies, save/load, inventory

### Goal

Coins, Experience and inventory work as runtime state.

### Implement

- Currency state: Coins, Experience.
- Inventory stacks.
- Add/remove item requests.
- Save DTO for currencies and inventory.
- View data for top bar and inventory popup.

### Acceptance criteria

- Runtime request can add Coins and EXP.
- Runtime request can add/remove items.
- Save/load restores currencies and inventory.
- UI only displays values and sends requests.

## 4. Stage 2 — Units and shop

### Goal

Игрок может купить owned unit за Coins.

### Implement

- `UnitTypeData`.
- Owned unit runtime state.
- `ShopOffer` generation.
- `BuyUnitRequest`.
- `UnitBoughtEvent`.
- Save/load for owned units and shop timer.
- Scene-authored shop and roster UI.

### Acceptance criteria

- Shop shows offers from authored unit types.
- Buying checks Coins in runtime.
- Bought unit appears in roster.
- Save/load restores owned units.

## 5. Stage 3 — Graveyard

### Goal

Graves create coin pickups, manual collect works.

### Implement

- Grave runtime state.
- Grave pickup timer.
- `CollectGravePickupRequest`.
- Coins reward.
- Graveyard view data.
- Manual collect UI.

### Acceptance criteria

- A test-created grave produces pickup after timer.
- Player collects pickup and gets Coins.
- Save/load restores grave timer and stored pickups.
- No offline catch-up happens on load.

## 6. Stage 4 — First gathering location

### Goal

Игрок добывает items через manual action и worker assignment.

### Implement

- `LocationData`.
- `LocationActionData`.
- Drop tables.
- `AssignUnitToLocationRequest`.
- `StartLocationActionRequest`.
- Action timers.
- `ClaimLocationDropRequest`.
- Basic location UI.

### Acceptance criteria

- Manual action creates item drop.
- Claim adds item to inventory.
- Assigned worker repeats action.
- Save/load restores assignment and timer.
- No offline gathering catch-up.

## 7. Stage 5 — Crafting and coal

### Goal

Игрок крафтит authored recipes, а unknown recipes дают `item_coal`.

### Implement

- `ItemData`.
- `PotionData`.
- `CraftRecipeData`.
- `RecipeKeyBuilder`.
- `RecipeResolver`.
- `CraftRecipeRequest`.
- `CraftCompletedEvent`.
- Recipe history save.
- Potion customization save.
- Craft UI and result feedback.

### Acceptance criteria

- `item_snake + item_bottle -> potion_poison`.
- `item_snake + item_cookie -> item_coal`.
- `item_snake + item_bottle + item_cookie -> item_coal` unless exact recipe exists.
- Inputs are consumed.
- Output is added to inventory.
- Tried recipe history saves and loads.
- Repeating tried unknown recipe shows preview `item_coal`.
- Inventory `item_coal` stack has no recipe metadata.

## 8. Stage 6 — Laboratory and death/EXP pipeline

### Goal

Игрок может убить юнита potion-ом, получить EXP за каждую смерть и создать grave.

### Implement

- `DeathCause` model.
- `DeathEffectData`.
- `DeathRewardRuleData`.
- `DeathRewardResolver`.
- Common death pipeline system.
- `PlaceUnitInLabRequest`.
- `RunExperimentRequest`.
- `UnitDiedEvent`.
- `ExperienceGrantedEvent`.
- `GraveCreatedEvent`.
- `DeathDiscoveryUnlockedEvent` optional.
- Laboratory UI.

### Acceptance criteria

- Lab unit does not lose HP/lifetime while waiting in lab slot.
- Running experiment consumes potion.
- Unit dies through common death pipeline.
- Potion death grants configured EXP every time.
- Repeating the same potion death still grants configured base EXP.
- Death creates grave.
- DeathDiscovery first-time state is saved but does not block repeat EXP.
- Save/load after death is stable.

## 9. Stage 7 — Upgrade tree

### Goal

Player spends Experience on upgrades that affect runtime.

### Implement

- `UpgradeData`.
- Upgrade runtime state.
- Cost validator.
- `BuyUpgradeRequest`.
- Upgrade effects for at least:
  - graveyard auto collect;
  - first gathering action speed;
  - lab notebook/death journal category;
  - coal recipe hints.
- Upgrade tree UI popup.

### Acceptance criteria

- Upgrade purchase validates EXP in runtime.
- EXP is spent.
- Upgrade effect changes runtime behavior.
- Save/load restores purchased upgrades.

## 10. Stage 8 — Lifetime death

### Goal

Lifetime expiration death grants EXP and creates grave.

### Implement

- Lifetime tick system.
- Lifetime death cause.
- Lifetime death reward rule.
- Death pipeline integration.

### Acceptance criteria

- Unit with lifetime <= 0 dies.
- Lifetime death grants configured EXP, e.g. 1.
- Death creates grave.
- Repeat lifetime deaths continue to grant configured EXP.
- No offline lifetime reduction.

## 11. Stage 9 — Hazardous location

### Goal

Опасная локация наносит damage/lifetime effects and uses common death pipeline.

### Implement

- `HazardData`.
- Damage tick system.
- Defense calculation.
- Lifetime influence in location.
- Hazard death reward rule.
- Hazard icons in location UI.

### Acceptance criteria

- Hazard damage applies by cooldown.
- Defense reduces damage to minimum 0.
- Lifetime influence works only while game is running.
- Death from hazard grants configured EXP.
- Death creates grave.
- No duplicate death/grave events.
- Save/load damaged units and hazard timers.

## 12. Stage 10 — Casino

### Goal

Casino provides paid item generation with no damage/lifetime effects.

### Implement

- Casino `LocationData`.
- Three slot machine action nodes.
- Coin cost per spin.
- Drop tables.
- Worker automation with cost checks.
- Collector automation.
- Casino UI.

### Acceptance criteria

- Manual spin costs Coins.
- Spin does not start without enough Coins.
- Spin creates item drop.
- Worker auto spin still pays Coins per spin.
- Collector can claim drops.
- Casino has no hazards and no lifetime influence.

## 13. Stage 11 — Death journal and recipe book

### Goal

Игрок видит историю смертей и рецептов.

### Implement

- Death journal view data.
- Recipe book view data.
- Known/tired recipes.
- Unknown recipe entries showing output coal after tried.
- Death repeat counts.
- EXP gained summaries.

### Acceptance criteria

- Death journal shows discovered entries and repeat counts.
- Death journal does not imply only new deaths give EXP.
- Recipe book shows tried unknown recipe -> coal.
- UI data comes from runtime/save, not UI-local state.

## 14. Stage 12 — Ending

### Goal

Final upgrade unlocks immortality ending.

### Implement

- `immortality_final` upgrade.
- Ending unlock state.
- Scene object appears/activates.
- `StartEndingRequest`.
- Ending popup/sequence.
- Stats screen.

### Acceptance criteria

- Final upgrade validates costs/prerequisites.
- Click starts ending once.
- Stats include:
  - playtime;
  - total deaths;
  - total EXP from deaths;
  - unique discoveries;
  - repeat deaths;
  - potions crafted;
  - coal created;
  - units bought;
  - graves created.
- Save/load after ending is stable.

## 15. Verification checklist for MVP

MVP is done when:

- player can buy a unit;
- player can gather items;
- authored recipe works;
- unknown recipe gives coal;
- coal can be used in authored recipe;
- lab potion death grants configured EXP;
- repeated same potion death grants configured EXP again;
- lifetime death grants configured EXP;
- death creates grave;
- grave grants Coins;
- upgrade can be bought with EXP;
- save/load restores all above;
- no offline progress is calculated.
