# 07. Clarifying Questions — resolved and open

## 1. Resolved decisions from latest update

### 1.1 Does every death grant EXP?

Resolved: **yes**.

Every unit death grants Experience. Amount is configured by game designer in `DeathRewardRuleData`.

Examples:

- lifetime death -> 1 EXP;
- potion_1 death -> 3 EXP;
- potion_4 death -> 5 EXP.

DeathDiscovery remains useful for journal/unlocks/stats, but it is not the gate for base EXP.

### 1.2 Are failed potions unique?

Resolved: **no**.

All unknown recipes produce the universal item:

```text
item_coal / Уголь
```

Coal is an ordinary inventory item and can be used in recipes. Inventory does not keep separate coal variants by recipe.

Recipe history can still remember that a tried recipe produced coal, so the UI can preview the result on repeat.

### 1.3 Does offline progress exist?

Resolved: **no**.

No offline progress, no wall-clock catch-up, no offline deaths, no offline gathering, no offline grave generation. Timers resume from saved values after load.

### 1.4 What was “переводческого урона” in casino?

Resolved: typo. Correct interpretation: **periodic damage**.

Casino rule:

- no periodic damage;
- no lifetime influence;
- economic cost only: Coins per spin.

## 2. Still open product questions

### 2.1 What are units fictionally?

Options:

1. Lab-grown assistants.
2. Cartoon interns with absurd consent paperwork.
3. Clones.
4. Homunculi.
5. Robots/constructs.

Recommended default: lab-grown assistants / cartoon interns. This supports dark comedy without realistic cruelty.

### 2.2 Visual tone of death

Question: how explicit should death presentation be?

Recommended default:

- no gore;
- stylized VFX;
- icons;
- comic science notes;
- tombstone snapshots.

### 2.3 Number of deaths for full game

Question: how many DeathDiscoveries should exist in the first full version?

Recommended target:

- MVP: 5-8 death effects;
- vertical slice: 12-20;
- first full version: 60-120;
- “hundreds” only if content production is realistic.

### 2.4 EXP economy curve

Because every death gives EXP, upgrade costs need careful tuning.

Open decisions:

- Should EXP rewards scale by potion tier only?
- Should repeat deaths have same reward or lower repeat multiplier?
- Should locations have EXP-per-minute caps?
- Should cheap lifetime deaths remain viable but slow?

Recommended MVP:

- repeats give same configured base EXP;
- balance through costs and potion requirements;
- do not add anti-farm caps until a real problem appears.

### 2.5 Craft slots

Recommended MVP:

- start with 2 slots;
- unlock 3 slots;
- 4 slots later only if needed.

### 2.6 Recipe matching

Resolved assumption unless changed later:

- order-insensitive;
- exact set and quantities;
- no partial matching.

Example:

```text
snake + bottle -> poison
snake + bottle + cookie -> coal, unless exact recipe exists
```

### 2.7 Potion naming

Question: when does naming popup appear?

Recommended:

- only the first time player crafts a potion output;
- not for coal;
- not for ordinary crafted items unless explicitly useful.

### 2.8 Can units heal?

Recommended MVP:

- no healing;
- healing/protection as later upgrades or items.

### 2.9 Can player rescue units from dangerous locations?

Recommended:

- yes, unassign action should exist;
- unit keeps current HP/lifetime;
- removing from location stops hazards;
- no instant death on unassign.

### 2.10 Do graves have limits?

Open:

- unlimited graves may clutter UI;
- soft cap could merge old graves into cemetery sections;
- storage cap for pickups is recommended.

Recommended MVP:

- unlimited grave records;
- visible grave list/grid can paginate/scroll;
- each grave has pickup storage cap.

### 2.11 Is there prestige/new game plus?

Recommended first version:

- no prestige;
- finite ending;
- optional post-ending stats.

### 2.12 How is final success graded?

Possible stats:

- playtime;
- total deaths;
- total EXP from deaths;
- unique discoveries;
- repeat deaths;
- units bought;
- graves created;
- coal created;
- potions crafted;
- upgrades purchased;
- ending rank.

## 3. Implementation assumptions to keep until changed

- `item_coal` is the only failed craft output.
- Any non-authored exact recipe returns `item_coal`.
- Every death grants EXP.
- Death reward values are authored data.
- DeathDiscovery is journal/progression metadata, not EXP gate.
- Offline progress is not implemented.
- Casino has no hazard damage and no lifetime influence.
- UI is passive and scene-authored.
- Save/load stores timers but does not catch up offline time.
