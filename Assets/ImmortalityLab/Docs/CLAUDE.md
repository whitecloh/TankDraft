# CLAUDE.md — Immortality Lab

Behavioral guidelines for LLM coding agents working on this Unity project.

## 1. Think before coding

Do not assume silently.

Before implementation:

- read `00_accepted_design_decisions.md` first;
- state assumptions if the task is ambiguous;
- update docs first if product/architecture contract changes;
- avoid speculative systems.

## 2. Simplicity first

Minimum code that solves the task.

Do not add:

- generic frameworks for one use case;
- future-proof abstractions without need;
- runtime UI factories;
- giant managers;
- duplicate pipelines;
- offline progress.

## 3. Surgical changes

Touch only what the task needs.

- Do not refactor unrelated code.
- Match existing style.
- Remove only unused code created by your own change.
- Mention unrelated problems instead of fixing them silently.

## 4. Runtime first

Priority:

1. gameplay runtime;
2. save/load;
3. data contract;
4. UI sync;
5. visuals.

## 5. Project hard lines

- UI is not source of truth.
- Gameplay logic belongs in systems/services/runtime models.
- ScriptableObjects contain authored data.
- Save DTO contains persistence state only.
- Player-created potion names are not IDs.
- Every unit death goes through common death pipeline.
- Every unit death grants Experience by `DeathRewardRuleData`.
- DeathDiscovery is not the gate for base EXP.
- Unknown craft recipe gives `item_coal`.
- `item_coal` is universal and not unique per failed recipe.
- Offline progress is not implemented.
- Casino has no periodic damage and no lifetime influence.

## 6. Goal-driven execution

Turn tasks into verifiable criteria.

Example:

```text
Goal: craft unknown recipe.
Verify:
- inputs are consumed;
- item_coal is added;
- recipe history records the tried key;
- inventory coal stack is generic;
- save/load restores history and inventory;
- UI did not resolve the recipe itself.
```

Example:

```text
Goal: process potion death.
Verify:
- unit dies through common death pipeline;
- DeathRewardRuleData grants configured EXP;
- repeat death grants EXP again;
- grave is created;
- death discovery/repeat state is updated;
- save/load restores the result.
```
