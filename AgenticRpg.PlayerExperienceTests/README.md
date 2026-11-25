# Player experience regression validation

From the repository root, restore and build with the .NET 10 SDK. Run the suite from the test project directory to select Microsoft.Testing.Platform.

```powershell
dotnet restore AgenticRpg.slnx -p:Configuration=Release
dotnet build AgenticRpg.slnx --configuration Release --no-restore
Push-Location AgenticRpg.PlayerExperienceTests
dotnet test --project AgenticRpg.PlayerExperienceTests.csproj --configuration Release --no-build
Pop-Location
```

The suite uses fake repositories and processors, real HTTP/SignalR application endpoints, bUnit components, and the existing campaign lifecycle tests. It covers response IDs/revisions, interleaved streams, handoffs, partial failures, retries, retention, scope leases, concurrent tabs, withdrawal persistence, ownership, private narratives, save races, existing-world edits, completion recovery, pending dice windows, durable restart interruption, conditional draft writes, transcript pagination/privacy, completion checkpoint failures, composer recovery, and shared weapon formulas.

The Improve next coverage adds queued/active cancellation, streaming and dice cancellation, ownership, terminal races, safe tool-write boundaries, durable cancellation/review, structured intent and server item effects, shared costs and target validation, persistent overview/journal privacy, suggestion expiry, Quick Create deduplication, lifecycle guards, mobile Chat defaults, and unread badges.

## Two-browser integration playtest

The opt-in host serves the real Blazor client with test-only identities and repositories. It makes no production storage or AI calls. After the Release build, start it from the repository root in one terminal.

```powershell
$env:RPG_BROWSER_PLAYTEST_SIGNAL = Join-Path (Get-Location) 'AgenticRpg.PlayerExperienceTests/obj/browser-host.txt'
Push-Location AgenticRpg.PlayerExperienceTests
dotnet test --project AgenticRpg.PlayerExperienceTests.csproj --configuration Release --no-build --filter-method AgenticRpg.PlayerExperienceTests.BrowserPlaytestHostTests.HostForTwoBrowserPlaytest
Pop-Location
```

Wait for `obj/browser-host.txt`, then run in another terminal with Playwright, Chrome, and Edge installed:

```powershell
# Optional: point PLAYWRIGHT_MODULE at an existing Playwright package directory.
node AgenticRpg.PlayerExperienceTests/two-browser-playtest.cjs
```

The script checks lobby-to-game navigation, two characters' private narratives, interleaved actions, disabled duplicate controls, offline/reconnect convergence, navigation without withdrawal, recovery without another introduction, all world/campaign save modes, finishing a character with equipment, persisted withdrawal, draft discovery/resume without another introduction, composer refresh recovery in both browsers, completed-result controls, and campaign form autosave recovery. It also checks Continue/Join, overview/journal, structured Rest submission, active cancellation and state review after refresh, conditional scrolling, and Quick Create with refresh through explicit world save, campaign creation, character review/finish, and lobby readiness. It removes the signal file to stop the host. Remove that file manually to stop an interrupted playtest; the host also times out after ten minutes. The opt-in host is skipped by the normal test suite.

## Delivery constraints

Publish and deploy the host with its matching WebAssembly assets. The GitHub workflow now restores, builds, runs the regression suite, and explicitly publishes `AgenticRpg/AgenticRpg/AgenticRpg.csproj`; its static assets include the matching client. Hub contracts and recovery APIs require the joint deployment.

The host provisions `AgenticRpgDb/ScopeRecovery` with partition key `/scopeId` using the existing Cosmos client and credentials, following the repository's existing create-if-missing convention. The account needs permission to create that container. Existing entity documents need no migration. Edited worlds add an optional completion-session marker so recovery can distinguish an already committed edit from an older world. Existing transient drafts and history cannot be backfilled.

Drafts, player-visible transcripts, deduplication records, and clean agent checkpoints persist until explicit draft discard or campaign deletion. Cosmos reads use bounded ordered transcript queries; the runtime keeps active work plus at most 100 completed responses per scope for 24 hours as a cache. Those cache limits do not delete durable history. Request records retain their original scope/player/client IDs across restarts.

A restarted host marks previously active requests Interrupted, preserves their last checkpointed text, and requires the player to acknowledge reviewing current state. It never restarts the action, replays tool calls, rerolls dice, or generates another introduction. Saves reconcile fixed entity IDs and world completion markers before persisting again. Stream text is checkpointed at most once per second and flushed on completion; an abrupt shutdown can lose the newest fraction of a second. Failed checkpoints remain visible and can be retried by reloading state. Only successful complete requests commit agent-session checkpoints.

The current single-server execution model is retained. This is durable data recovery, not durable active-action resumption or coordination between multiple server instances. Checkpoint payloads remain server-only; HTTP/hub transcripts use validated character visibility and exclude GM-only content, including for the campaign owner. Discovery rules for locations/NPCs/quests are unchanged.

Composer drafts and uncertain submission IDs are isolated by authenticated account and scope in browser storage; logout clears that account's entries. Campaign forms autosave edits with the draft revision and reject stale tabs. Completed drafts show their saved result and disable generation/finalization.

WeaponMechanics intentionally preserves the live combat formulas, including both existing skill contributions and multiplication of the rolled total on critical hits. The sheet, dice explanations, combat result breakdowns, documentation, and agent instructions now agree. No balance or progression changes were made.

Live AI output and live Cosmos DB behavior are outside the fake-backed playtest. Restore still reports the pre-existing `Microsoft.OpenApi` 2.0.0 vulnerability warning. Production deployment has not been performed from this workspace.

Validated on 2026-10-07: 70 deterministic/lifecycle/component/HTTP/SignalR/dependency tests passed in Debug and Release; the expanded Chrome and Edge playtest passed without browser exceptions. The opt-in host is a separate passing run. The full Release solution build and host/client publish also passed.

Cosmos query references: [ordered TOP queries](https://learn.microsoft.com/en-us/cosmos-db/query/top), [parameterized queries](https://learn.microsoft.com/en-us/cosmos-db/query/parameterized-queries).

## Improve next implementation validation — October 9, 2026

Queued cancellation prevents execution and permits restoring or retrying text. Active cancellation stops future work, waits for an in-flight tool write, retains applied effects and partial narration, closes associated dice prompts, discards incomplete agent checkpoints, and requires state review. Completion and cancellation serialize before committing a successful checkpoint. A terminal outcome must persist before the pending request lock is released; a failed checkpoint requires reload/recovery.

Contextual actions carry additive `PlayerActionIntent` data alongside readable text through SignalR and durable records. The server derives the actor from the authenticated binding and revalidates canonical selections, ownership, turns, quantities and resources. GM tools perform effects. Unknown item effects and consumption remain explicit GM adjudication. Transport-uncertain retries reuse the original ID; deliberate retries after terminal failure use a new ID after review. No effect is automatically replayed.

Startup and creation keep the journey and curated starter choice in durable session context. Quick Create uses one stable request ID per session and suppresses the competing introduction. Saving remains an explicit player decision. Existing records without these additive fields remain readable. Insulting comedy remains mandatory.

The final Release regression suite passed: 95 tests, zero failures, and one skipped opt-in browser host. The full Chrome and Edge playtest passed with no browser exceptions. The Release solution build passed with zero errors, and the host publish succeeded with matching `AgenticRpg.Client` WebAssembly assets. Publish output is under the ignored test `obj/improve-next-publish` directory; nothing was deployed. Build warnings include the existing `Microsoft.OpenApi` vulnerability warning. Tests and browser playtests use fake storage and processors; live AI and Cosmos were not exercised.

## Onboarding style validation — October 10, 2026

The shared host `wwwroot/app.css` loads once before isolated component styles. Model-dialog section rules are scoped to `.model-menu-content`, so they no longer remove startup card borders and padding. Shared onboarding controls are scoped to `.rpg-onboarding`; page CSS handles responsive layout. The setup page, step indicator, startup entry/forms, Home start control, contextual actions and labelled Advanced Settings controls have explicit theme styles.

The two-browser playtest now checks computed backgrounds, form/button themes, minimum control heights, startup card padding/borders, and horizontal overflow at 1440px, 390px and 320px. It saves screenshots under ignored `obj/style-screenshots`. To run the focused checks against the opt-in host, use `node AgenticRpg.PlayerExperienceTests/onboarding-style-playtest.cjs`; this also checks Home, settings label fit, single global stylesheet loading and model-dialog section styles, and stops the host when done.

The expanded full Chrome/Edge playtest and final focused style checks passed without browser exceptions. Home, creation/gameplay Advanced Settings labels, model-dialog styles, and responsive onboarding all passed in both browsers. The Release regression suite passed 95 tests with one skipped opt-in host, and the Release solution build passed with zero errors. These checks use the local fake-backed host.

## Creation layout follow-up — October 10, 2026

Creation journey progress, Quick Create guidance, draft status and completed-result controls now render inside the themed creation page. Shared runtime controls style Reconnect and state-review actions. Campaign setup uses one full-width panel when no world-builder chat is rendered; world creation retains its split view. Flex viewport sizing accounts for the header/progress/status rows instead of subtracting fixed header heights. The lobby's journey indicator also belongs inside its page container.

The shared composer uses a narrow-screen grid, with submission controls below the text field. Both input modes keep Cancel/Send inside their panel. The shared model picker has dark fallback colours, and campaign model selection is grouped under Advanced Settings.

The new `creation-style-playtest.cjs` is included at the end of the full Chrome/Edge script and can run independently against the opt-in host. It checks campaign/world/character creation at 1440px, 390px and 320px; progress and draft-status placement/colours; absence of document overflow; available campaign width; scrolling to Save; both composer modes without clipped controls; and model-picker colours. Screenshots are under ignored `obj/style-screenshots`.

Final validation for the creation follow-up: the expanded complete Chrome/Edge playtest passed without browser exceptions; focused creation checks also passed in both browsers; 95 Release regression tests passed with one skipped opt-in host; and the Release solution build passed with zero errors. Mobile checks include both composer modes and Save access at 320px and 390px.

## Cosmos recovery query follow-up — October 10, 2026

The reported live `JoinSession` failure originated in `RecoveryRepository.InfoAsync`: Cosmos rejected the `SELECT VALUE` object containing `SUM` and `MAX`. Recovery metadata now uses separate partition-scoped, parameterized scalar aggregate queries, following the documented [SUM query form](https://learn.microsoft.com/en-us/cosmos-db/query/sum). Empty/undefined results preserve revision zero and no save timestamp, and the reader follows empty continuation pages.

Three new tests exercise the production repository with mocked Cosmos SDK containers and feeds, protecting the query shape, scope parameters, partition key, populated/empty results and continuation handling. Earlier in-memory repository tests bypassed the SQL and could not detect this failure. These mocks verify emitted queries and result handling; they do not execute the Cosmos query engine.

Validation: 98 Release tests passed, one opt-in browser host skipped, zero failures. The full Release solution build passed with zero errors and four existing warnings. Live Cosmos was not rerun, and no deployment was performed.

## Creation chat space follow-up — October 10, 2026

World/campaign/character creation now put journey steps and draft details behind one compact header dropdown. Character requirements live in the same dropdown instead of wrapping the Finish controls onto extra rows. The dropdown overlays the page, so opening it does not reduce panel height. The current journey step remains visible in its summary. Mobile headers and view switches use less padding while retaining 44px controls.

Durable chat suggestions now use one collapsed row with a scrollable overlay containing the complete original action text. Opening the choices leaves transcript height unchanged; submission guards and suggestion relevance remain unchanged. The new-content button sits above this row. Chat message lists can shrink correctly inside their flex containers, and the character connection strip uses less padding.

The browser fixture includes four long creation suggestions matching the reported layout problem. Creation checks cover 1440×900, 1280×720, 390×844 and 320×740, assert a transcript of at least 35% of viewport height, and verify setup/suggestion overlays do not shrink it. Component coverage checks compact progress, retained requirements and full suggestion submission. The Release regression suite passed 100 tests with one skipped opt-in host; the full Release solution build passed with zero errors.

The final complete Chrome/Edge playtest passed with no browser exceptions, including all compact creation checks and existing Continue/Join, Quick Create through save/lobby, cancellation, recovery and conditional scrolling flows. The separate opt-in host test also passed. Screenshots are saved under ignored `obj/style-screenshots`.

## Contextual action selector removal — October 10, 2026

At the user's request, the action selector component and stylesheet were deleted, along with its selected-action state, submission/selection handlers, character/inventory/spell/combat launch buttons, component callbacks and shared button CSS. Its component test was removed. The browser playtest asserts the controls are absent and submits the party Rest request through the regular chat composer. Durable suggestions remain available. Shared server rules and structured-action recovery/retry contracts still have active consumers and remain compatible with existing saved messages.

Validation: 99 tests passed, one opt-in host skipped, zero failures; the full Release solution build passed with zero errors. The repository's updated test dependencies use xUnit's executable runner. Validation ran without changing project or SDK configuration:

```powershell
dotnet AgenticRpg.PlayerExperienceTests/bin/Release/net10.0/AgenticRpg.PlayerExperienceTests.dll -noColor -noLogo
# Opt-in browser host after building Release:
$env:RPG_BROWSER_PLAYTEST_SIGNAL = Join-Path (Get-Location) 'AgenticRpg.PlayerExperienceTests/obj/browser-host.txt'
dotnet AgenticRpg.PlayerExperienceTests/bin/Release/net10.0/AgenticRpg.PlayerExperienceTests.dll -noColor -noLogo -method AgenticRpg.PlayerExperienceTests.BrowserPlaytestHostTests.HostForTwoBrowserPlaytest
```

The updated complete Chrome/Edge playtest and separate host test passed. It confirms the selector/launch controls are absent, chat Rest submission works, and existing onboarding, save, cancellation/recovery, conditional scrolling and creation layout checks still pass without browser exceptions.
