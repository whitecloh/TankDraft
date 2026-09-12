# 05. Data and Content Spec — Immortality Lab

## 1. ID conventions

Recommended stable IDs:

```text
unit_intern
unit_gravedigger
item_snake
item_bottle
item_cookie
item_milk
item_coal
potion_poison
potion_poisoned_milk_drink
death_lifetime_expired
death_poison
damage_radiation
hazard_radiation_zone
location_shop
location_graveyard
location_lab
location_craft
location_casino
upgrade_graveyard_auto_collect
```

IDs must not use player-written names.

## 2. Enums

Suggested enums:

```csharp
public enum ItemCategory
{
    Material,
    Potion,
    CraftedItem,
    FailedResult,
    CurrencyLike,
    UnlockKey
}

public enum DeathCauseType
{
    Experiment,
    Damage,
    Lifetime,
    Special
}

public enum DeathRewardMatchType
{
    Potion,
    DeathEffect,
    Hazard,
    DamageType,
    Lifetime,
    Special,
    Fallback
}

public enum UnitAssignmentType
{
    Idle,
    LocationWorker,
    LocationCollector,
    GraveyardCaretaker,
    Laboratory,
    Dead
}
```

## 3. ScriptableObject contracts

### 3.1 GameSettingsData

Purpose: global values.

Fields:

```text
startingCoins
startingExperience
startingCraftSlots
maxCraftSlots
simulationTickIntervalSeconds
autosaveIntervalSeconds
defaultShopRefreshSeconds
defaultGravePickupSeconds
fallbackDeathRewardRuleId
unknownRecipeOutputItemId = item_coal
```

Do not add offline progress settings.

### 3.2 UnitTypeData

Fields:

```text
unitTypeId
displayName
localizedDescription
icon
bodySprite/animationSet
rarity
shopPrice
maxHpMin/maxHpMax
lifespanSecondsMin/lifespanSecondsMax
baseWorkSpeedMultiplier
baseCollectSpeedMultiplier
resistances: List<DamageResistanceData>
tags
unlockConditions
shopWeight
canWork
canCollect
canBeExperimentSubject
graveVisualOverride optional
```

### 3.3 DamageTypeData

Fields:

```text
damageTypeId
displayName
icon
color
defaultDeathEffectId
defaultRewardRuleId optional
statusEffectId optional
description
```

Examples:

- `damage_radiation`
- `damage_mutation`
- `damage_poison`
- `damage_fire`
- `damage_entropy`

### 3.4 ItemData

Fields:

```text
itemId
displayName
category
icon
rarity
description
maxStack
canBeCraftIngredient
canBeUsedInLab
potionDataId optional
```

Important item:

```text
item_coal
category: FailedResult or Material
canBeCraftIngredient: true
```

`item_coal` is universal. It does not have per-recipe variants.

### 3.5 PotionData

Potion can be represented as ItemData category `Potion` plus PotionData, or as a separate asset linked from ItemData.

Fields:

```text
potionId
itemId
visualId
icon
color
labDeathEffectId
consumeOnExperiment = true
allowedAsIngredient = true/false
defaultHiddenNameKey optional
```

Player custom names/descriptions are not stored here.

### 3.6 CraftRecipeData

Fields:

```text
recipeId
inputs: List<ItemAmountData>
outputItemId
outputQuantity
craftDurationSeconds
requiredCraftSlotCount
requiredUpgradeIds
isSecret
discoveryTier
```

Rules:

- recipe key is order-insensitive;
- quantity matters;
- exact input set required;
- unknown recipe output is not authored here; it is `GameSettingsData.unknownRecipeOutputItemId`.

Recipe key format example:

```text
item_bottle:1|item_snake:1
```

### 3.7 DeathEffectData

Purpose: describes a death effect for journal/presentation.

Fields:

```text
deathEffectId
displayName
hiddenDisplayName
icon
visualEffectId
category
sourceType
sourcePotionId optional
sourceHazardId optional
sourceDamageTypeId optional
```

No mandatory XP here. XP comes from `DeathRewardRuleData`.

### 3.8 DeathRewardRuleData

Purpose: designer-authored EXP and reward rule for death.

Fields:

```text
deathRewardRuleId
priority
matchType: DeathRewardMatchType
matchSourceId
experiencePerDeath
firstDiscoveryBonusExperience
coinsBonus
itemDrops: List<ItemDropData>
graveIncomeMultiplier
discoveryId optional
```

Matching examples:

```text
matchType Potion, matchSourceId potion_poison -> 3 EXP
matchType Potion, matchSourceId potion_poisoned_milk_drink -> 5 EXP
matchType Lifetime, matchSourceId lifetime_expired -> 1 EXP
matchType Hazard, matchSourceId hazard_radiation_zone -> 2 EXP
matchType Fallback, matchSourceId * -> 1 EXP
```

### 3.9 DeathDiscoveryData

Purpose: journal entry and first-time discovery.

Fields:

```text
deathDiscoveryId
deathEffectId
titleHidden
titleRevealed
descriptionRevealed
category
icon
sourceHint
unlockConditions optional
endingWeight optional
```

DeathDiscovery does not decide base EXP.

### 3.10 LocationData

Fields:

```text
locationId
displayName
sceneRegionId
isUnlockedAtStart
unlockConditions
actionNodes
workerSlots
collectorSlots
hazards
lifetimeInfluence
storageCapacity
visualStateIds
```

### 3.11 LocationActionData

Fields:

```text
actionId
displayName
durationSeconds
coinCost
requiredWorkerTags optional
dropTable
autoRepeatAllowed
manualAllowed
```

Casino slot machine example:

```text
actionId: casino_slot_machine_1
coinCost: 5
durationSeconds: 2.0
dropTable: casino drops
hazards: none
lifetimeInfluence: none
```

### 3.12 HazardData

Fields:

```text
hazardId
displayName
damageTypeId
damageAmount
cooldownSeconds
effectId optional
affectsLifetime
lifetimeLossAmount
lifetimeLossCooldownSeconds
rewardRuleOverrideId optional
```

### 3.13 UpgradeData

Fields:

```text
upgradeId
displayName
description
icon
costs: List<CostData>
prerequisites
unlockConditions
effects: List<UpgradeEffectData>
isFinalImmortalityUpgrade
```

## 4. Runtime models

### 4.1 OwnedUnitState

```text
runtimeUnitId
unitTypeId
displayName
maxHp
currentHp
lifetimeSecondsRemaining
resistances
traits
assignmentType
assignedLocationId optional
assignedSlotId optional
statusEffects
deathState optional
```

### 4.2 InventoryStackState

```text
itemId
quantity
```

No recipe metadata for `item_coal` stacks.

### 4.3 PotionCustomizationState

```text
potionId
customName
customDescription
lastEditedTick
```

### 4.4 RecipeHistoryState

```text
recipeKey
outputItemId
outputQuantity
isAuthoredRecipe
attemptCount
firstTriedTick
lastTriedTick
```

Unknown recipe example:

```text
recipeKey: item_cookie:1|item_snake:1
outputItemId: item_coal
isAuthoredRecipe: false
```

### 4.5 DeathCauseRuntime

```text
deathCauseType
sourceId
damageTypeId optional
deathEffectId optional
locationId optional
potionId optional
hazardId optional
playtimeSeconds
```

### 4.6 DeathRecordState

A single death event record or aggregate depending on save size.

Fields:

```text
deathRecordId
runtimeUnitId
unitTypeId
deathCause
experienceGranted
firstDiscoveryBonusGranted
createdGraveId
tick/playtime
```

### 4.7 DeathDiscoveryState

```text
deathDiscoveryId
isDiscovered
firstDiscoveredTick
firstUnitTypeId
firstRuntimeUnitId optional
repeatCount
totalExperienceFromThisDeathType
```

### 4.8 GraveState

```text
graveId
unitNameSnapshot
unitTypeId
iconOverride optional
deathCauseSummary
experienceGrantedOnDeath
pickupTimerRemaining
storedPickupCount
coinValueMultiplier
createdTick
```

### 4.9 LocationRuntimeState

```text
locationId
isUnlocked
actionStates
assignedWorkerUnitIds
assignedCollectorUnitIds
storedDrops
timers
hazardTickState
```

### 4.10 ShopState

```text
currentOffers
refreshTimerRemaining
lastGeneratedSeed optional
```

## 5. Save DTO

Top-level save:

```text
saveVersion
playtimeSeconds
currencies
ownedUnits
inventoryStacks
potionCustomizations
recipeHistory
deathRecords optional or aggregate stats
deathDiscoveries
graves
locations
shopState
upgradeState
endingState
rngState optional
```

No offline progress fields.

## 6. Starter content tables

### 6.1 Items

| ID | Category | Source |
|---|---|---|
| item_snake | Material | gathering |
| item_bottle | Material | gathering |
| item_cookie | Material/CraftedItem | gathering/casino |
| item_milk | Material | gathering/casino |
| item_poisoned_cookie | CraftedItem | craft |
| item_coal | FailedResult | unknown recipe |
| item_strange_liquid | Material | gathering |
| item_bone_dust | Material | graveyard/gathering |

### 6.2 Recipes

| Recipe ID | Inputs | Output |
|---|---|---|
| recipe_poison | item_snake + item_bottle | potion_poison |
| recipe_poisoned_cookie | potion_poison + item_cookie | item_poisoned_cookie |
| recipe_poisoned_milk | item_poisoned_cookie + item_milk | potion_poisoned_milk_drink |
| recipe_unstable_coal | item_coal + item_strange_liquid | potion_unstable_mixture |

Unknown exact combinations output `item_coal`.

### 6.3 Death rewards

| Rule ID | Match Type | Source | EXP |
|---|---|---|---:|
| death_reward_lifetime | Lifetime | lifetime_expired | 1 |
| death_reward_poison | Potion | potion_poison | 3 |
| death_reward_poisoned_milk | Potion | potion_poisoned_milk_drink | 5 |
| death_reward_unstable | Potion | potion_unstable_mixture | 4 |
| death_reward_radiation | Hazard | hazard_radiation_zone | 2 |
| death_reward_fallback | Fallback | * | 1 |

### 6.4 Upgrades

| ID | Cost | Effect |
|---|---:|---|
| upgrade_graveyard_auto_collect | 3 EXP | unlock caretaker slot |
| upgrade_better_shovels | 3 EXP | first gathering action -20% duration |
| upgrade_lab_notebook | 4 EXP | death journal categories visible |
| upgrade_thrift_hiring | 5 EXP | shop prices -10% |
| upgrade_coal_recycling | 5 EXP | coal recipe hints |
| upgrade_immortality_final | high | ending unlock |

## 7. Balance notes

Because every death gives EXP:

- early upgrades should cost more than 1 EXP unless first death flow needs fast gratification;
- cheap deaths need low EXP;
- potion deaths can have higher EXP because they consume crafted resources;
- lifetime deaths should be useful but not optimal farming;
- fallback death reward should be nonzero to honor “any death grants EXP”.
