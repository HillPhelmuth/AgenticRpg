---
agent: 'agent'
description: 'Refactor combat tools and related code files'
---

# Refactor Rpg Combat

Change how `AITool`s related to starting and executing RPG combat operate. Includes replacing `Combatant` and using new `RpgMonster` type, generating monsters from `DndMonsterService`

## Tasks

- Refactor `CombatEncounter` and it's uses
 - Replace `Combatants` uses with `RpgMonster` and `Character`, and removing `List<Combatant> Combatants` property.
 - Creating `CombatEncounter` now requires using `DndMonsterService` to create `RpgMonster`(s) before initiating combat.

- Refactor `CombatTools` to create seperate `AITool` functions for playar character --> monsters and monster --> player character attacks.
- Refactor `CombatAgent` and `GameMasterAgent` to utilize new tools. Includes prompt changes to instruct model on combat creation and execution flow.
 