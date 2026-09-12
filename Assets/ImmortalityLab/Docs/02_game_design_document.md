# 02. Game Design Document — Immortality Lab

## 1. High concept

**Immortality Lab** — активная incremental/management игра на одном экране. Игрок — учёный, который хочет добиться бессмертия. Путь к бессмертию проходит через исследование смертности: покупку юнитов, добычу ингредиентов, крафт зелий, смертельные эксперименты, опасные локации, кладбище и дерево прокачки.

Ключевая формула:

```text
Смерть не останавливает прогресс. Смерть производит данные, EXP и могилы.
```

## 2. Pillars

### 2.1 Death is progress

Любая смерть юнита полезна:

- выдаёт Experience по настроенному правилу;
- создаёт grave;
- попадает в death log;
- может открыть DeathDiscovery;
- может влиять на финальную статистику.

### 2.2 One screen laboratory machine

Игра происходит на одной основной сцене. Локации — отдельные области/панели этой сцены. Popup используются для деталей, но не являются отдельными gameplay scenes.

### 2.3 Experimentation without hard failure

Крафт unknown recipe не проваливается в пустоту. Игрок получает `item_coal`, который можно использовать как обычный ингредиент.

### 2.4 Automation as progression

Ранний game loop ручной. Позже игрок назначает юнитов на auto work и auto collect.

### 2.5 Dark comedy, not gore

Тон — абсурдная лабораторная тёмная комедия. Смерти должны быть stylized, icon-based, VFX-based, но не реалистично жестокие.

## 3. Core loop

```text
1. Купить юнита в магазине.
2. Назначить юнита на добывающую локацию.
3. Собрать item drops.
4. Скрафтить potion или получить coal.
5. Использовать potion в лаборатории или потерять юнита в hazard/lifetime.
6. Death pipeline выдаёт EXP и создаёт grave.
7. Grave создаёт coin collectables.
8. Coins покупают новых юнитов, actions, locations.
9. EXP покупает upgrades.
10. Upgrades открывают новые локации, рецепты, automation и финальный путь к immortality.
```

## 4. Player progression

### Early game

- Доступны Shop, Graveyard, Laboratory, Craft, первая безопасная добыча.
- Игрок покупает дешёвых юнитов.
- Первые рецепты 2-slot.
- Первые смерти дают понятный EXP.
- Первое upgrade открывает auto collect или ускорение добычи.

### Mid game

- Открываются casino, hazardous location, новые items и potions.
- Появляются damage types, defenses, lifetime pressure.
- Игрок назначает workers и collectors.
- Крафт начинает использовать coal и multi-step chains.

### Late game

- Игрок оптимизирует EXP/Coins flow.
- Локации требуют специальных юнитов или защит.
- Upgrade tree приближает финальный `immortality_final`.
- Открывается ending object на сцене.

## 5. Currencies and resources

### 5.1 Coins

Используются для:

- покупки юнитов;
- открытия некоторых локаций;
- запуска платных actions, например casino spin;
- части upgrades.

Источники:

- grave collectables;
- location rewards;
- events;
- optional death rewards, если настроено.

### 5.2 Experience

Валюта прокачки.

Главное правило:

```text
Каждая смерть даёт Experience по DeathRewardRuleData.
```

Примеры:

| Death cause | EXP |
|---|---:|
| Lifetime expired | 1 |
| Potion 1 | 3 |
| Potion 4 | 5 |
| Radiation hazard | 2 |

Experience source не должен быть hardcoded в UI. Runtime получает `DeathCause`, выбирает `DeathRewardRuleData` и начисляет награду.

### 5.3 Items

Ингредиенты, unlock materials и crafting outputs.

Примеры:

- `item_snake`
- `item_bottle`
- `item_cookie`
- `item_milk`
- `item_coal`
- `item_bone_dust`
- `item_casino_chip`
- `item_radioactive_soil`

### 5.4 Potions

Potions — особый тип craft output, который может применяться в лаборатории и/или участвовать в дальнейших рецептах.

Potion имеет authored ID и visual. Игрок может задать custom name и description, но это cosmetic save data, не gameplay ID.

## 6. Units

### 6.1 Назначение

Юниты — runtime-сущности, которые:

- покупаются в Shop;
- работают в локациях;
- собирают outputs;
- работают cemetery caretaker-ами;
- помещаются в лабораторию;
- умирают;
- превращаются в graves.

### 6.2 Параметры owned unit

- `RuntimeUnitId` — уникальный ID экземпляра.
- `UnitTypeId` — ссылка на authored тип.
- `DisplayName`.
- `MaxHp`.
- `CurrentHp`.
- `LifetimeSecondsRemaining`.
- `Defenses` — защита по damage types.
- `Traits`.
- `CurrentAssignment`.
- `WorkSpeedMultiplier`.
- `CollectionSpeedMultiplier`.
- `StatusEffects` optional.

### 6.3 Death conditions

Юнит погибает, если:

```text
CurrentHp <= 0
```

или

```text
LifetimeSecondsRemaining <= 0
```

или если эксперимент/специальное событие вызывает guaranteed death.

### 6.4 Lab safety

Пока юнит находится в lab subject slot, он не теряет HP и lifetime passively. Смерть происходит только после запуска эксперимента.

### 6.5 Примерные unit types

| UnitType | Роль | Особенность |
|---|---|---|
| Intern | стартовый дешёвый юнит | низкое HP, низкая цена |
| Volunteer | универсал | среднее HP/lifetime |
| Gravedigger | кладбище | быстрее auto collect graves |
| Chemist | крафт/лаборатория | бонус к hints или craft speed |
| Gambler | казино | bonus к casino action speed/cost |
| Hazmat Worker | опасные зоны | высокая radiation/mutation defense |

## 7. Locations

### 7.1 Общий контракт локации

Каждая location имеет:

- `LocationId`;
- scene region;
- unlock conditions;
- action nodes;
- worker slots;
- collector slots optional;
- hazard list;
- lifetime influence;
- output storage;
- visual state.

### 7.2 Доступно с начала

1. Shop.
2. Graveyard.
3. Laboratory.
4. Craft.
5. First gathering location.

### 7.3 Shop

Назначение: покупка юнитов.

Правила:

- открыт с начала;
- содержит несколько `ShopOffer`;
- refresh происходит по timer;
- покупка стоит Coins;
- купленный юнит появляется в owned unit pool;
- stock зависит от unlocked unit types.

MVP:

- 3 offers;
- auto-refresh;
- без ручного reroll, если не требуется балансом.

### 7.4 Graveyard

Назначение: passive/active income от умерших юнитов.

Правила:

- каждая смерть создаёт grave;
- grave хранит snapshot: unit name, type, death cause, EXP gained, timestamp;
- grave раз в N секунд создаёт coin collectable;
- игрок кликает collectable и получает Coins;
- caretaker unit может собирать collectables автоматически;
- поздние upgrades улучшают pickup value, interval, storage cap.

### 7.5 Laboratory

Назначение: проводить эксперименты с potions.

UI:

- unit slot;
- potion inventory/slot;
- apply button;
- experiment feedback;
- EXP feedback;
- death journal feedback.

Правила:

1. Игрок помещает живого свободного юнита в lab slot.
2. Пока юнит в lab slot, он не теряет HP/lifetime.
3. Игрок выбирает potion.
4. Experiment consumes potion.
5. ExperimentResolver определяет `DeathCause` / `DeathEffect`.
6. Юнит умирает.
7. Death pipeline начисляет EXP по `DeathRewardRuleData`.
8. Death pipeline создаёт grave.
9. DeathDiscovery обновляется, если это первый такой discovery.

Повторный эксперимент с тем же potion всё равно выдаёт configured EXP.

### 7.6 Craft

Назначение: создавать potions/items из ингредиентов.

UI:

- ingredient slots;
- craft button;
- known preview;
- result display;
- potion naming popup для новых potion outputs;
- recipe book.

Правила:

- recipes authored by designer;
- input normalizes by item IDs and counts;
- recipe exact-match required;
- known authored recipe -> configured output;
- unknown recipe -> `item_coal`;
- `item_coal` is a normal item and can be used in recipes;
- recipe history stores tried recipe keys and outputs;
- player custom potion name/description is save-only cosmetic data.

Канонический пример:

```text
Authored:
item_snake + item_bottle = potion_poison
potion_poison + item_cookie = item_poisoned_cookie
item_poisoned_cookie + item_milk = potion_poisoned_milk_drink

Runtime:
item_snake + item_bottle -> potion_poison
item_snake + item_cookie -> item_coal
item_snake + item_milk + item_cookie -> item_coal
item_snake + item_bottle + item_cookie -> item_coal
```

### 7.7 Gathering locations

Общий action flow:

1. Игрок кликает action object.
2. Запускается action duration.
3. После завершения создаётся item drop.
4. Игрок кликает drop, чтобы добавить item в inventory.
5. Worker slot запускает action автоматически.
6. Collector slot собирает drops автоматически.
7. Hazards воздействуют на assigned units, если location hazardous.

### 7.8 First safe gathering location

Рабочее название: `Backyard`.

Правила:

- открыта с начала;
- нет damage;
- нет lifetime penalty;
- даёт стартовые items;
- один worker slot открывается early.

### 7.9 Casino

Назначение: платная добыча items через слот-машины.

Правила:

- открывается позже;
- 3 slot machines;
- один spin стоит Coins;
- spin запускает animation и drop table roll;
- dropped item нужно claim-ить;
- worker slot может auto spin, но каждый spin всё равно списывает Coins;
- collector slot может auto claim;
- **нет периодического damage**;
- **нет lifetime influence**.

### 7.10 Radioactive zone

Назначение: опасная добыча редких items.

Пример:

- action: digging;
- drop table: item A 45%, item B 35%, item C 20%;
- hazard radiation: 10 damage/sec;
- hazard mutation: 5 damage/sec;
- lifetime reduction every K seconds;
- defenses reduce incoming damage by percent.

Если юнит умирает в зоне, смерть всё равно выдаёт EXP по `DeathRewardRuleData`, создаёт grave и записывается в log.

## 8. Damage, lifetime and hazards

### 8.1 Damage tick

Hazard data:

```text
DamageTypeId
DamageAmount
CooldownSeconds
EffectId optional
```

Damage formula:

```text
FinalDamage = max(0, IncomingDamage * (1 - DefensePercent / 100))
```

### 8.2 Lifetime influence

Location может ускорять расход lifetime:

```text
Every K seconds: LifetimeSecondsRemaining -= Amount
```

Death cause для lifetime:

```text
DeathCauseType = Lifetime
SourceId = lifetime_expired or location-specific lifetime hazard
```

Lifetime death тоже выдаёт EXP.

## 9. Death and EXP system

### 9.1 Common death pipeline

Любая смерть идёт через единый pipeline:

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

Запрещено удалять unit напрямую из UI, location script или lab view.

### 9.2 DeathCause

DeathCause хранит:

- `DeathCauseType`: Experiment, Damage, Lifetime, Special;
- `SourceId`: potion ID, hazard ID, location ID or special ID;
- `DamageTypeId` optional;
- `DeathEffectId` optional;
- `LocationId` optional;
- timestamp/playtime.

### 9.3 DeathRewardRuleData

Определяет награду за смерть.

Поля:

- `DeathRewardRuleId`;
- `Priority`;
- `MatchType`: Potion, DeathEffect, Hazard, DamageType, Lifetime, Special, Fallback;
- `MatchSourceId`;
- `ExperiencePerDeath`;
- `FirstDiscoveryBonusExperience` optional;
- `CoinsBonus` optional;
- `ItemDrops` optional;
- `GraveIncomeMultiplier` optional;
- `JournalDiscoveryId` optional.

Runtime выбирает самое конкретное подходящее правило.

### 9.4 DeathDiscovery

DeathDiscovery — first-time journal entry. Он не блокирует EXP за повтор.

Хранит:

- discovery ID;
- first discovered tick;
- first unit;
- source potion/hazard/location;
- repeat count;
- revealed text/icon;
- category.

### 9.5 Примеры rewards

| Rule | Match | EXP |
|---|---|---:|
| lifetime_default | Lifetime | 1 |
| poison_potion_death | potion_poison | 3 |
| radioactive_overdose | hazard_radiation_zone | 2 |
| unstable_implosion | potion_unstable_mixture | 5 |
| fallback_any_death | any death | 1 |

## 10. Upgrade tree

### 10.1 Общий принцип

Дерево ветвистое. Узлы стоят Experience, Coins или items. Финальный узел — `immortality_final`.

### 10.2 Ветки

#### Units

- больше HP;
- больше lifetime;
- стартовые defenses;
- дешевле shop;
- быстрее work/collect.

#### Locations

- открыть новые локации;
- добавить worker slots;
- ускорить actions;
- улучшить drop tables;
- снизить hazards.

#### Graveyard

- быстрее grave pickups;
- больше Coins;
- caretaker slot;
- больше storage cap.

#### Laboratory

- unlock death journal hints;
- улучшить EXP reward multiplier;
- unlock experiment filters;
- optional second lab slot late game.

#### Craft

- больше ingredient slots;
- known recipe preview;
- recipe hints;
- recipes using coal;
- unlock special recipes.

### 10.3 Victory

Финальный узел:

```text
immortality_final
```

После покупки:

1. На сцене появляется ending object.
2. Игрок кликает.
3. Запускается ending sequence.
4. Показываются титры и statistics.

## 11. UI/UX

### 11.1 Main scene

Основные areas:

- top bar: Coins, Experience, immortality progress;
- Shop;
- Roster;
- Graveyard;
- Laboratory;
- Craft;
- gathering locations;
- side buttons: Inventory, Recipe Book, Knowledge Tree, Death Journal, Stats, Settings.

### 11.2 Popups

Popups:

- Inventory;
- Recipe Book;
- Potion Naming;
- Unit Picker;
- Upgrade Tree;
- Death Journal;
- Ending Stats.

Popup не является source of truth. Он показывает view data и отправляет requests.

### 11.3 Required feedback

- unit bought;
- unit assigned;
- item gathered;
- authored recipe crafted;
- unknown recipe -> coal;
- potion named/skipped;
- experiment started;
- unit died;
- EXP gained;
- grave created;
- grave collectable ready;
- upgrade bought;
- location unlocked;
- ending unlocked.

## 12. Save / Load

Save stores:

- currencies;
- owned units;
- assignments;
- inventory stacks;
- potion custom names/descriptions;
- known/tried recipes;
- death discoveries;
- death statistics;
- graves;
- location unlock/runtime state;
- upgrade state;
- shop stock and remaining refresh timer;
- active action timers;
- RNG state/seed if deterministic rolls are needed.

Save does not store:

- scene object references;
- transient animation state;
- popup state as gameplay truth;
- calculated view data;
- offline progress results.

Offline progress is not simulated on load.

## 13. MVP scope

MVP готов, если игрок может:

1. Купить юнита.
2. Добыть предметы в безопасной локации.
3. Скрафтить минимум 2 authored результата.
4. Скрафтить unknown recipe и получить `item_coal`.
5. Использовать `item_coal` в authored recipe.
6. Убить юнита в лаборатории potion-ом.
7. Получить EXP за смерть по configured rule.
8. Получить EXP за lifetime death или hazard death, если такая смерть доступна в MVP.
9. Увидеть grave.
10. Собрать Coins с grave.
11. Купить upgrade за EXP.
12. Сохранить и загрузить игру без потери core state.

## 14. Starter content

### 14.1 Items

| ID | Source |
|---|---|
| item_snake | first gathering / later biome |
| item_bottle | first gathering |
| item_cookie | shop/casino/gathering |
| item_milk | gathering/casino |
| item_coal | unknown recipe |
| item_bone_dust | graveyard / gathering |
| item_strange_liquid | gathering |

### 14.2 Potions and recipes

| Recipe ID | Inputs | Output |
|---|---|---|
| recipe_poison | item_snake + item_bottle | potion_poison |
| recipe_poisoned_cookie | potion_poison + item_cookie | item_poisoned_cookie |
| recipe_poisoned_milk | item_poisoned_cookie + item_milk | potion_poisoned_milk_drink |
| recipe_unstable_coal | item_coal + item_strange_liquid | potion_unstable_mixture |
| fallback_unknown | any non-authored exact combination | item_coal |

### 14.3 Death rewards

| Rule ID | Match | EXP |
|---|---|---:|
| death_lifetime_expired | Lifetime | 1 |
| death_poison | potion_poison | 3 |
| death_poisoned_milk | potion_poisoned_milk_drink | 5 |
| death_unstable_mixture | potion_unstable_mixture | 4 |
| death_fallback | any unmatched death | 1 |

### 14.4 Upgrades

| ID | Cost | Effect |
|---|---:|---|
| graveyard_auto_collect | 3 EXP | unlock caretaker slot |
| better_shovels | 3 EXP | first gathering action -20% duration |
| lab_notebook | 4 EXP | death journal categories visible |
| thrift_hiring | 5 EXP | shop prices -10% |
| coal_recycling | 5 EXP | unlock coal recipe hints |
| immortality_final | high | triggers ending |

## 15. Not in MVP

- prestige / new game plus;
- offline progress;
- complex diseases/mutations as long-term simulation;
- 4+ ingredient slots;
- multiple failed result item families;
- realistic gore;
- fully procedural recipe generation.
