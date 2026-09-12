# 00. Accepted Design Decisions v2

Этот документ имеет приоритет над старыми формулировками в остальных документах, если где-то останется конфликт. Перед кодом Codex должен читать его первым.

## 1. Experience за смерть

### Решение

**Любая смерть юнита даёт Experience.**

Experience не ограничен первым открытием уникального death discovery. Каждая смерть проходит через общий death pipeline и получает награду из authored data.

Примеры дизайнерских правил:

| Death source | Example config | EXP |
|---|---:|---:|
| Lifetime expired | `death_lifetime_expired` | 1 |
| Potion 1 experiment | `potion_1` / `death_potion_1` | 3 |
| Potion 4 experiment | `potion_4` / `death_potion_4` | 5 |
| Radiation hazard | `hazard_radiation_zone` | 2 |
| Mutation damage | `damage_mutation` fallback | 2 |

### Runtime implication

Death pipeline должен выглядеть так:

```text
UnitDeathDetected
-> ResolveDeathCause
-> ResolveDeathRewardRule
-> GrantExperience
-> RegisterDeathDiscoveryOrRepeat
-> CreateGrave
-> RemoveUnitFromAssignments
-> Emit UnitDiedEvent / ExperienceGrantedEvent / GraveCreatedEvent
-> Save
```

`GrantExperience` вызывается для каждой смерти.

### DeathDiscovery после изменения

`DeathDiscovery` больше не означает “единственный источник XP”. Теперь это:

- запись в журнале смертей;
- first-time discovery marker;
- категория/иконка/текст открытия;
- условие для unlocks, achievements, ending grading;
- optional first-time bonus, если дизайнер явно настроит такой бонус.

Повторная смерть:

- снова выдаёт base EXP по `DeathRewardRuleData`;
- увеличивает repeat count;
- не создаёт новый first-time discovery record;
- может иметь дополнительный repeat multiplier, если дизайнер его заведёт.

## 2. DeathRewardRuleData

Нужен отдельный authored config для награды за смерть.

Рекомендуемый приоритет matching:

1. Specific potion/death effect rule.
2. Specific hazard rule.
3. Specific damage type rule.
4. Lifetime rule.
5. Global fallback death rule.

Рекомендуемые поля:

```text
deathRewardRuleId
priority
matchType: Potion | DeathEffect | Hazard | DamageType | Lifetime | Special | Fallback
matchSourceId
experiencePerDeath
firstDiscoveryBonusExperience
coinsBonus
itemDrops optional
graveIncomeMultiplier
journalDiscoveryId optional
```

Минимальное правило MVP:

```text
fallback_any_death -> 1 EXP
lifetime_expired -> 1 EXP
potion_1 -> 3 EXP
potion_4 -> 5 EXP
```

## 3. Failed craft output

### Решение

Unknown recipe не создаёт уникальное failed potion. Unknown recipe выдаёт один универсальный предмет:

```text
item_coal
Display name: Уголь
Category: Item
```

`item_coal` — обычный item. Он может быть ингредиентом authored recipes.

### Что считается unknown recipe

Recipe считается unknown/failed, если normalized input key не найден в authored `CraftRecipeData`.

Пример authored recipes:

```text
item_snake + item_bottle -> potion_poison
potion_poison + item_cookie -> item_poisoned_cookie
item_poisoned_cookie + item_milk -> potion_poisoned_milk_drink
```

Примеры результата:

```text
item_snake + item_bottle -> potion_poison
item_snake + item_cookie -> item_coal
item_snake + item_milk + item_cookie -> item_coal
item_snake + item_bottle + item_cookie -> item_coal
```

Последний пример даёт `item_coal`, потому что точного рецепта `snake + bottle + cookie` нет, даже если подмножество `snake + bottle` является валидным рецептом.

### Recipe history

Recipe history может сохранять tried recipe key:

```text
recipeKey: item_cookie:1|item_snake:1
outputId: item_coal
isAuthoredRecipe: false
attemptCount: 1
```

Это нужно для UI preview при повторе. Но inventory stack `item_coal` не хранит recipe key и не становится уникальным предметом.

## 4. Offline progress

### Решение

Offline progress не нужен.

Когда игра закрыта:

- grave pickups не генерируются за прошедшее реальное время;
- shop refresh не догоняет время оффлайн;
- workers не добывают предметы оффлайн;
- hazards не убивают юнитов оффлайн;
- life time не уменьшается оффлайн.

Save/load должен восстановить timer state таким, каким он был на момент сохранения. После загрузки симуляция продолжается с текущего runtime time.

Запрещено добавлять:

- `maxOfflineProgressSeconds`;
- offline simulation service;
- догоняющие расчёты по wall-clock time;
- death processing while app was closed.

## 5. Казино и опечатка

Фраза “нет переводческого урона” в исходном описании считается опечаткой.

Правильное правило:

```text
В казино нет периодического урона и нет влияния на lifetime.
Каждый spin стоит Coins.
```

Казино может убивать экономику игрока, но не юнитов.

## 6. Приоритет для Codex

Если старый документ говорит:

- “XP только за новую смерть”;
- “repeat death не даёт XP”;
- “failed potion хранит recipe metadata в inventory”;
- “offline progress possible/later”;

то это устарело. Использовать правила из этого документа.
