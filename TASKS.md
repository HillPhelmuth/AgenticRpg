# Tasks

## 2025-12-21
- Update `InventoryItem` references to support polymorphic item types (`WeaponItem`, `ArmorItem`), including equipped slot typing and JSON serialization.

## 2025-12-22
- Make `Character.ArmorClass` a calculated property (base 10 + Agility modifier + armor/shield bonuses) and remove all direct setters; align `ArmorClassBonus` handling and legacy conversion.
- Split `ItemList.json` into `ItemList.json` (non-weapon/non-armor), plus new `WeaponList.json` and `ArmorList.json` using typed properties; update loader to merge all lists.
- Refactor economy tool DTOs to use `InventoryItem` / `WeaponItem` / `ArmorItem` (remove redundant `ShopItem` and `ItemDetailInfo`).
- Update spell and item data to support `MagicEffect` and `MagicItem` (migrate `ClericSpellList.json`/`WizardSpellsList.json`, add `MagicItemList.json`, and embed resources).

## Discovered During Work
- (none)

## 2026-01-01
- [x] Fix dice rolling multi-window flow: `RollDiceHubService.RequestDiceRoll` must honor `numberOfRollWindows` and not return until each window has submitted a result.
- [x] Reduce noisy dice-roll server logs: missing modal reference in `GameHub.SubmitDiceRollResult` is now logged at Debug (common when multiple clients receive the same broadcast).
- [x] Fix agent-triggered dice roll deadlock: submit dice results over a separate SignalR connection so `SubmitDiceRollResult` can run while the original hub invocation is still in-flight.

## 2026-01-04
- [x] Refactor RPG combat tools and encounter model: remove `CombatEncounter.Combatants`, use `PartyCharacters` + `EnemyMonsters` (`RpgMonster`), generate monsters via `DndMonsterService`, and split combat attack tools into player→monster and monster→player.

## 2026-01-05
- [x] Add model dropdown text filter on StartupMenu (filter models as user types).

## 2026-01-08
- [x] Share `AgentThread` instances by scope (session/campaign) and `AgentType` so all requests use the same chat history.
- [x] Fix AI tool duplication across messages: ensure `RawRepresentationFactory` returns a fresh `ChatCompletionOptions` instance per request so tool definitions don"t accumulate.

## Discovered During Work
- (Fixed) Build failures due to ambiguous `IAgentThreadStore` namespace resolution; resolved by fully-qualifying in `BaseGameAgent`.

## 2026-01-09
- [x] Refactor spell attacks to auto-hit and use monster saving throws to reduce damage/debuffs by half.
- [x] Fix multiplayer lobby start sync: ensure cached `GameState` is hydrated with full campaign characters before broadcasting state updates; fix Cosmos game-state lookup by `CampaignId`.
- [x] Preserve multiplayer ready states on rejoin: `JoinCampaign` no longer resets `PlayerReadyStatuses` for existing players.
- [x] Target dice-roll prompts to the acting player: include `PlayerId` on dice-roll requests and ignore non-matching requests client-side (including spell save prompts).
