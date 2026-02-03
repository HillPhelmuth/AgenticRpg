---
agent: 'agent'
description: 'Refactor level-up tools and related code files'
---

# Refactor Rpg Level-Up into Rpg Character Manager
Change `AITool`s related to leveling up RPG characters into tools to manager the character more generally (including level-up). This includes updating character stats generally, primarily in [CharacterLevelUpTools](../../AgenticRpg.Core\Agents\Tools\CharacterLevelUpTools.cs).

Do not rename any files.

## Level-Up Rules

- MaxHP and MaxMP are now calculated properties based on character level and attributes. Remove any direct setting of these properties during level-up.
- Characters gain 2 skill points at level-up. Each point can be be used to either increase a `Skill` by 1 or unlock a new `Skill`.
- Magic users only gain spells at certain levels. Update the level-up process to check character class and level before granting new spells.
 - Wizards and Clerics gain new spells at even levels (e.g 2, 4, 6, etc.)
 - War Mages and Paladins gain new spells at levels divisible by 3 (e.g 3, 6, 9, etc.)

 ## Character Manager Concepts

 - Should encapsulate all character-related operations, including level-up, stat updates, skill management, spell management, and any other `Character` related functionality.
 - Should accomplish this with the fewest number of tools while keeping it simple for the LLM to invoke and for the developer to maintain.
 - Should be designed to be used by `CharacterManagerAgent` as a way to fix previous mistakes by the GM found by the GM or players or otherwise by player concensus.

 ## Tasks

 - Refactor `CharacterLevelUpTools` to update/add/remove level-up methods to follow new rules.
 - Add new methods for general `Character` management (stat updates, skill management, spell management).