# Tasks

## 2026-01-31
- [x] Compact the model dropdown with in-panel filtering and transient popup messages.
- [x] Limit StartupMenu campaign visibility to owner or invitation code.
- [x] Enforce campaign ownership in API endpoints.
- [x] Send access tokens on client API calls.
- [x] Align API base address to same origin.

## 2026-02-01
- [x] Cache TTS audio in blob storage by `messageId` to avoid regenerating speech for repeated sends.

## 2026-02-04
- [x] Make `RedirectToLogin.razor` force a full-page navigation to `Account/Login?redirectUri=/` (equivalent to clicking the `Login.razor` link).

## 2026-01-29
- [x] Add PCM audio JS interop for text-to-speech playback.
- [x] Stream TTS audio from server to client via SignalR for Game chat.

## 2026-01-27
- Add DI/logging to `AgenticRpg.DevUtility` (create `ServiceCollection` and `ILoggerFactory` in `Program.cs`).

## 2026-01-23
- [x] Trigger combat video generation in `EndCombat` without blocking the combat completion response.

## 2026-01-21
- [x] Refactor `CharacterManagerTools.GetMaxSpellLevelForCharacterLevel` to use a mathematical calculation instead of a hardcoded level table.

## 2026-01-19
- Refactor CombatTools dice roll and combat-attack helpers to reduce duplication and simplify attack flows.
- [x] Refactor CharacterLevelUpTools into character manager with updated level-up rules and management tools.

## 2025-12-21
- Update `InventoryItem` references to support polymorphic item types (`WeaponItem`, `ArmorItem`), including equipped slot typing and JSON serialization.

## 2025-12-22
- Make `Character.ArmorClass` a calculated property (base 10 + Agility modifier + armor/shield bonuses) and remove all direct setters; align `ArmorClassBonus` handling and legacy conversion.
- Split `ItemList.json` into `ItemList.json` (non-weapon/non-armor), plus new `WeaponList.json` and `ArmorList.json` using typed properties; update loader to merge all lists.
- Refactor economy tool DTOs to use `InventoryItem` / `WeaponItem` / `ArmorItem` (remove redundant `ShopItem` and `ItemDetailInfo`).
- Update spell and item data to support `MagicEffect` and `MagicItem` (migrate `ClericSpellList.json`/`WizardSpellsList.json`, add `MagicItemList.json`, and embed resources).

## Discovered During Work
- (none)

## 2026-01-18
- [x] Refactor agent instructions to use prompt templates and context variables across all agents.
- [x] Split agent context prompts into distinct template variables.

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
