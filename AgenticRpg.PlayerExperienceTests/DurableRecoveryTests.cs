using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class DurableRecoveryTests
{
    private static GameRuntimeCoordinator Runtime(TestRuntime f, IRecoveryRepository repository) => new(f.Processor, f.Hub.Object,
        f.States.Object, f.Sessions, f.Broadcaster, NullLogger<GameRuntimeCoordinator>.Instance, new(), recovery: repository);
    private static RecoveryDocument Record(RuntimeMessage m) => new() { ScopeId = m.ScopeId, Id = m.Id, Kind = "message",
        OwnerId = m.PlayerId, Version = m.Revision, UpdatedAt = m.UpdatedAt, Payload = JsonSerializer.Serialize(m, RecoveryJson.Options) };

    [Theory]
    [InlineData(MessageProcessingStatus.Queued, ProcessingPhase.Queued)]
    [InlineData(MessageProcessingStatus.Processing, ProcessingPhase.Generating)]
    [InlineData(MessageProcessingStatus.Processing, ProcessingPhase.WaitingForDice)]
    [InlineData(MessageProcessingStatus.Processing, ProcessingPhase.Saving)]
    public async Task RestartRetainsPartialTextAndRequiresReviewWithoutReplaying(MessageProcessingStatus status, ProcessingPhase phase)
    {
        var f = new TestRuntime(); var repository = new InMemoryRecoveryRepository();
        var message = new RuntimeMessage { ScopeId = "campaign", Id = "response", ClientMessageId = "client", PlayerId = "alice",
            Content = "Partial narration", Revision = 7, Status = status, Phase = phase };
        await repository.WriteAsync(Record(message), 0);
        var restarted = Runtime(f, repository);
        var page = await restarted.TranscriptAsync("campaign");
        var recovered = Assert.Single(page.Messages);
        Assert.Equal("Partial narration", recovered.Content);
        Assert.Equal(MessageProcessingStatus.Interrupted, recovered.Status);
        Assert.True(recovered.IsComplete);
        Assert.Equal(8, recovered.Revision);
        Assert.Empty(f.Processor.Requests);
        Assert.True(await restarted.RequiresReviewAsync("campaign", "alice"));
        var retried = await restarted.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "client", false);
        Assert.Equal("response", retried.Id);
        Assert.Empty(f.Processor.Requests);
        await Assert.ThrowsAsync<HubException>(() => restarted.SubmitAsync("campaign", "alice", "a", "Alice", "New", AgentType.None, "new", false));
        await restarted.ReviewAsync("campaign", "alice");
        Assert.False(await restarted.RequiresReviewAsync("campaign", "alice"));
        var again = Runtime(f, repository);
        await again.RecoverAsync("campaign");
        Assert.False(await again.RequiresReviewAsync("campaign", "alice"));
    }

    [Fact]
    public async Task RequestsAreDurableBeforeExecutionAndCannotBeExecutedIfAcceptanceFails()
    {
        var f = new TestRuntime(); var unavailable = new CompletionFailureRepository(new InMemoryRecoveryRepository()) { FailAll = true };
        var runtime = Runtime(f, unavailable);
        await Assert.ThrowsAsync<IOException>(() => runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "client", false));
        Assert.Empty(f.Processor.Requests);
        var accepted = await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "durable", false);
        Assert.NotNull(await f.Recovery.GetAsync("campaign", accepted.Id));
        Assert.Single(f.Processor.Requests);
        f.Processor.Complete("durable");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
    }

    [Fact]
    public async Task DraftsSurviveRestartKeepEquipmentTypesAndRejectStaleWrites()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = TestRuntime.Character();
        session.Context.DraftCharacter.Inventory.Add(new WeaponItem { Name = "Bow", WeaponType = WeaponType.Ranged, DamageDice = "1d6" });
        session.Context.CampaignId = "campaign"; session.SelectedModel = "chosen-model";
        await f.Sessions.UpdateSessionStateAsync(session);
        var restarted = new SessionStateManager(NullLogger<SessionStateManager>.Instance, f.Hub.Object, new(), f.Recovery);
        var restored = (await restarted.GetSessionStateAsync(session.SessionId))!;
        Assert.Equal("chosen-model", restored.SelectedModel);
        Assert.Equal("campaign", restored.Context.CampaignId);
        Assert.IsType<WeaponItem>(Assert.Single(restored.Context.DraftCharacter!.Inventory));
        var stale = JsonSerializer.Deserialize<SessionState>(JsonSerializer.Serialize(restored, RecoveryJson.Options), RecoveryJson.Options)!;
        restored.Context.DraftCharacter.Name = "Saved new name";
        await restarted.UpdateSessionStateAsync(restored);
        await Assert.ThrowsAsync<RecoveryConflictException>(() => restarted.UpdateSessionStateAsync(stale));
        await restarted.DeleteSessionAsync(session.SessionId);
        Assert.Empty(await f.Recovery.ListAsync(session.SessionId));
        Assert.Null(await new SessionStateManager(NullLogger<SessionStateManager>.Instance, f.Hub.Object, new(), f.Recovery).GetSessionStateAsync(session.SessionId));
    }

    [Fact]
    public async Task SaveReconcilesEntityAlreadyWrittenBeforeRestartWithoutAddingEquipmentAgain()
    {
        var f = new TestRuntime(); var session = await f.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = TestRuntime.Character(); session.Context.PersistenceEntityId = "persisted-result"; session.FinalizationPending = true;
        await f.Sessions.UpdateSessionStateAsync(session);
        f.CharacterRecords["persisted-result"] = TestRuntime.Character("persisted-result");
        var restarted = new SessionStateManager(NullLogger<SessionStateManager>.Instance, f.Hub.Object, new(), f.Recovery);
        var completion = new SessionCompletionService(restarted, f.Characters.Object, f.Worlds.Object, f.Campaigns.Object, f.Threads.Object,
            new(), f.Hub.Object, NullLogger<SessionCompletionService>.Instance, Mock.Of<ICampaignQuestPreparation>());
        Assert.Equal("persisted-result", await completion.FinalizeCharacterAsync(session.SessionId));
        f.Characters.Verify(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = (await f.Recovery.GetAsync(session.SessionId, "session"))!.Read<SessionState>();
        Assert.True(saved.IsCompleted); Assert.Equal("persisted-result", saved.ResultEntityId);
        Assert.Equal("persisted-result", await completion.FinalizeCharacterAsync(session.SessionId));
    }

    [Fact]
    public async Task TranscriptPaginationIsStableAndFiltersPrivateAndGmEntries()
    {
        var f = new TestRuntime(); var repository = new InMemoryRecoveryRepository();
        for (var i = 0; i < 125; i++) await repository.WriteAsync(Record(new RuntimeMessage { ScopeId = "campaign", Id = $"m-{i:000}",
            ClientMessageId = $"c-{i}", PlayerId = "alice", Revision = 1, Status = MessageProcessingStatus.Completed, Timestamp = DateTime.UnixEpoch.AddMinutes(i) }), 0);
        foreach (var (id, visibility, target) in new[] { ("gm", NarrativeVisibility.GMOnly, ""), ("private-a", NarrativeVisibility.CharacterSpecific, "a"), ("private-b", NarrativeVisibility.CharacterSpecific, "b") })
            await repository.WriteAsync(Record(new() { ScopeId = "campaign", Id = id, ClientMessageId = id, PlayerId = "alice", Revision = 1,
                Visibility = visibility, TargetCharacterId = target, Status = MessageProcessingStatus.Completed }), 0);
        var runtime = Runtime(f, repository);
        var first = await runtime.TranscriptAsync("campaign", characterId: "a");
        Assert.Equal(50, first.Messages.Count); Assert.Contains(first.Messages, m => m.Id == "private-a");
        Assert.DoesNotContain(first.Messages, m => m.Id is "gm" or "private-b");
        var second = await runtime.TranscriptAsync("campaign", first.Cursor, characterId: "a");
        var third = await runtime.TranscriptAsync("campaign", second.Cursor, characterId: "a");
        Assert.Equal(126, first.Messages.Concat(second.Messages).Concat(third.Messages).Select(m => m.Id).Distinct().Count());
        Assert.Null(third.Cursor); Assert.True(runtime.HasAcceptedRequests("campaign"));
        await runtime.DeleteScopeAsync("campaign"); Assert.Empty(await repository.ListAsync("campaign"));
    }

    [Fact]
    public async Task DraftAndTranscriptHttpRoutesRejectImpersonation()
    {
        await using var app = new PlayerAppFactory();
        var session = await app.Fakes.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        using var alice = app.Client("alice"); using var bob = app.Client("bob"); using var anonymous = app.Client();
        Assert.Single((await alice.GetFromJsonAsync<List<DraftSummary>>("/api/sessions"))!);
        Assert.Empty((await bob.GetFromJsonAsync<List<DraftSummary>>("/api/sessions"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/scopes/{session.SessionId}/transcript")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/sessions/{session.SessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/sessions/{session.SessionId}")).StatusCode);
        Assert.Empty(await app.Fakes.Recovery.ListAsync(session.SessionId));
    }

    [Fact]
    public async Task CompletionCheckpointFailureKeepsDraftAndRetryReconcilesWithoutDuplicateWrites()
    {
        var f = new TestRuntime(); var repository = new CompletionFailureRepository(f.Recovery);
        var manager = new SessionStateManager(NullLogger<SessionStateManager>.Instance, f.Hub.Object, new(), repository);
        var session = await manager.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = TestRuntime.Character(); await manager.UpdateSessionStateAsync(session);
        var completion = new SessionCompletionService(manager, f.Characters.Object, f.Worlds.Object, f.Campaigns.Object, f.Threads.Object,
            new(), f.Hub.Object, NullLogger<SessionCompletionService>.Instance, Mock.Of<ICampaignQuestPreparation>());
        repository.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => completion.FinalizeCharacterAsync(session.SessionId));
        Assert.False(session.IsCompleted); Assert.True(session.FinalizationPending);
        Assert.Single(f.CharacterRecords);
        var inventoryCount = f.CharacterRecords.Values.Single().Inventory.Count;
        repository.Fail = false;
        await completion.ReconcileAsync(session.SessionId);
        Assert.True(session.IsCompleted);
        Assert.Equal(inventoryCount, f.CharacterRecords.Values.Single().Inventory.Count);
        f.Characters.Verify(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AgentCheckpointsCommitOnlyAtSuccessfulRequestBoundaries()
    {
        var repository = new InMemoryRecoveryRepository();
        var store = new InMemoryAgentSessionStore(repository);
        using var json = JsonDocument.Parse("{\"version\":1,\"history\":[\"clean\"]}");
        store.Stage("scope", AgentType.GameMaster, json.RootElement);
        Assert.Empty(await repository.ListAsync("scope", "agent"));
        await store.CommitScopeAsync("scope");
        var checkpoint = Assert.Single(await repository.ListAsync("scope", "agent"));
        Assert.Contains("clean", checkpoint.Payload);
        using var partial = JsonDocument.Parse("{\"history\":[\"unfinished tool call\"]}");
        store.Stage("scope", AgentType.GameMaster, partial.RootElement);
        store.DiscardScope("scope"); await store.CommitScopeAsync("scope");
        Assert.Equal(checkpoint.Payload, (await repository.GetAsync("scope", checkpoint.Id))!.Payload);
    }

    [Fact]
    public async Task StreamCheckpointsAreThrottledAndTerminalTextIsFlushed()
    {
        var f = new TestRuntime(); var clock = new ManualClock();
        var runtime = new GameRuntimeCoordinator(f.Processor, f.Hub.Object, f.States.Object, f.Sessions, f.Broadcaster,
            NullLogger<GameRuntimeCoordinator>.Instance, new(), clock, f.Recovery);
        var accepted = await runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "stream", false);
        var request = Assert.Single(f.Processor.Requests);
        await request.StatusCallback!(new() { Status = MessageProcessingStatus.Processing });
        await request.StreamCallback!(new() { Token = "First ", AgentType = "GameMaster" });
        await request.StreamCallback!(new() { Token = "second ", AgentType = "GameMaster" });
        Assert.Empty((await f.Recovery.GetAsync("campaign", accepted.Id))!.Read<RuntimeMessage>().Content);
        clock.Advance();
        await request.StreamCallback!(new() { Token = "third", AgentType = "GameMaster" });
        Assert.Equal("First second third", (await f.Recovery.GetAsync("campaign", accepted.Id))!.Read<RuntimeMessage>().Content);
        f.Processor.Complete("stream");
        await TestRuntime.UntilAsync(() => !runtime.HasPending("campaign"));
        await TestRuntime.UntilAsync(() => f.Events.Any(e => e.Method == "MessageUpdated" && ((RuntimeMessage)e.Args[0]!).IsComplete));
        Assert.True((await f.Recovery.GetAsync("campaign", accepted.Id))!.Read<RuntimeMessage>().IsComplete);
    }
    [Fact]
    public async Task ReloadRetriesFailedCheckpointWithANewRevisionWithoutExecutingAgain()
    {
        var f = new TestRuntime(); var repository = new CompletionFailureRepository(new InMemoryRecoveryRepository());
        var runtime = Runtime(f, repository);
        await runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "checkpoint", false);
        repository.FailAll = true;
        f.Processor.Complete("checkpoint");
        await TestRuntime.UntilAsync(() => f.Events.Any(e => e.Method == "MessageUpdated" && ((RuntimeMessage)e.Args[0]!).IsComplete));
        var failed = Assert.Single(runtime.Snapshot("campaign"));
        Assert.Equal("checkpoint_failed", failed.Failure!.Code);
        repository.FailAll = false;
        var restored = Assert.Single((await runtime.TranscriptAsync("campaign")).Messages);
        Assert.Null(restored.Failure); Assert.True(restored.Revision > failed.Revision);
        Assert.Single(f.Processor.Requests);
        Assert.True((await repository.GetAsync("campaign", restored.Id))!.Read<RuntimeMessage>().IsComplete);
    }

    [Fact]
    public async Task ToolProgressReportsDiceAndResolutionWithoutDependingOnAgentConstruction()
    {
        var f = new TestRuntime(); var progress = new RuntimeProgress();
        var runtime = new GameRuntimeCoordinator(f.Processor, f.Hub.Object, f.States.Object, f.Sessions, f.Broadcaster,
            NullLogger<GameRuntimeCoordinator>.Instance, new(), recovery: f.Recovery, progress: progress);
        await runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Attack", AgentType.None, "phases", false);
        var request = Assert.Single(f.Processor.Requests);
        await request.StatusCallback!(new() { Status = MessageProcessingStatus.Processing });
        var dice = new RollDiceHubService(f.Hub.Object, progress: progress);
        var parameters = new AgenticRpg.DiceRoller.Models.RollDiceParameters(); parameters.Set("PlayerId", "alice");
        var rolling = dice.RequestDiceRoll("campaign", parameters, new() { Title = "Attack roll" });
        Assert.Equal(ProcessingPhase.WaitingForDice, Assert.Single(runtime.Snapshot("campaign")).Phase);
        Assert.Contains("alice", Assert.Single(runtime.Snapshot("campaign")).ProgressDetail!);
        var pending = Assert.Single(dice.GetPending("campaign", "alice"));
        dice.Submit(new AgenticRpg.DiceRoller.Models.RollDiceResults(true) { SessionId = "campaign", RequestId = pending.WindowRequestIds![0] }, "alice");
        await rolling;
        Assert.Equal(ProcessingPhase.Resolving, Assert.Single(runtime.Snapshot("campaign")).Phase);
        await progress.ReportAsync("campaign", ProcessingPhase.Saving);
        Assert.Equal(ProcessingPhase.Saving, Assert.Single(runtime.Snapshot("campaign")).Phase);
        f.Processor.Complete("phases");
        await TestRuntime.UntilAsync(() => !runtime.HasPending("campaign"));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now += TimeSpan.FromSeconds(1);
    }
    private sealed class CompletionFailureRepository(IRecoveryRepository inner) : IRecoveryRepository
    {
        public bool Fail { get; set; }
        public bool FailAll { get; set; }
        public Task<RecoveryDocument?> GetAsync(string scopeId, string id) => inner.GetAsync(scopeId, id);
        public Task<List<RecoveryDocument>> ListAsync(string? scopeId = null, string? kind = null, string? ownerId = null) => inner.ListAsync(scopeId, kind, ownerId);
        public Task DeleteScopeAsync(string scopeId) => inner.DeleteScopeAsync(scopeId);
        public Task WriteAsync(RecoveryDocument document, long expectedVersion) => FailAll || Fail && document.Kind == "session" && document.Read<SessionState>().IsCompleted
            ? Task.FromException(new IOException("Storage failure")) : inner.WriteAsync(document, expectedVersion);
    }

    [Fact]
    public async Task CampaignTranscriptUsesValidatedCharacterAndPrivateBroadcastReachesBothOwnedTabs()
    {
        await using var app = new PlayerAppFactory(); var f = app.Fakes;
        f.CharacterRecords["a"] = TestRuntime.Character(); f.CharacterRecords["b"] = TestRuntime.Character("b", "bob");
        f.State.Characters = f.CharacterRecords.Values.ToList();
        foreach (var (id, visibility, target) in new[] { ("public", NarrativeVisibility.Global, ""), ("gm", NarrativeVisibility.GMOnly, ""), ("alice", NarrativeVisibility.CharacterSpecific, "a"), ("bob", NarrativeVisibility.CharacterSpecific, "b") })
            await f.Recovery.WriteAsync(Record(new RuntimeMessage { ScopeId = "campaign", Id = id, ClientMessageId = id, PlayerId = "alice", Revision = 1,
                Visibility = visibility, TargetCharacterId = target, Status = MessageProcessingStatus.Completed }), 0);
        using var alice = app.Client("alice"); using var bob = app.Client("bob");
        var first = await alice.GetFromJsonAsync<TranscriptPage>("/api/scopes/campaign/transcript");
        Assert.Equal(new[] { "alice", "public" }, first!.Messages.Select(m => m.Id).Order().ToArray());
        var second = await bob.GetFromJsonAsync<TranscriptPage>("/api/scopes/campaign/transcript");
        Assert.Equal(new[] { "bob", "public" }, second!.Messages.Select(m => m.Id).Order().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/scopes/campaign/transcript?characterId=b")).StatusCode);
        f.Connections.Attach("alice-one", new("campaign", "alice", "a", "Alice", false, "one"));
        f.Connections.Attach("alice-two", new("campaign", "alice", "a", "Alice", false, "two"));
        f.Connections.Attach("bob-one", new("campaign", "bob", "b", "Bob", false, "one"));
        await f.Broadcaster.MessageAsync(new() { ScopeId = "campaign", Visibility = NarrativeVisibility.CharacterSpecific, TargetCharacterId = "a" });
        Assert.Equal(new[] { "alice-one", "alice-two" }, f.Events.Where(e => e.Method == "MessageUpdated").Select(e => e.Target).Order().ToArray());
        var count = f.Events.Count;
        await f.Broadcaster.MessageAsync(new() { ScopeId = "campaign", Visibility = NarrativeVisibility.GMOnly });
        Assert.Equal(count, f.Events.Count);
    }

    [Theory]
    [InlineData(WeaponType.Melee, false)]
    [InlineData(WeaponType.Ranged, false)]
    [InlineData(WeaponType.Both, false)]
    [InlineData(WeaponType.Melee, true)]
    public void MechanicsPreserveLiveAttackAndDamageFormulas(WeaponType type, bool finesse)
    {
        var c = TestRuntime.Character(); c.Skills["Melee Combat"] = 3; c.Skills["Ranged Combat"] = 2;
        var w = new WeaponItem { Name = "Test", WeaponType = type, DamageDice = "1d6+2", WeaponProperties = finesse ? ["Finesse"] : [] };
        var legacy = StatsCalculator.CalculateAttackBonus(c, w);
        if (type is WeaponType.Melee or WeaponType.Both) legacy += 3; else legacy += 2;
        var shared = WeaponMechanics.For(c, w);
        Assert.Equal(legacy, shared.Attack);
        Assert.Equal(4 + legacy + w.GetDieRollBonus(), shared.Damage(4, false));
        Assert.Equal(8 + legacy + w.GetDieRollBonus(), shared.Damage(4, true));
        Assert.Contains(WeaponMechanics.Signed(legacy + w.GetDieRollBonus()), shared.DamageExplanation);
        Assert.Equal("-2", WeaponMechanics.Signed(-2));
    }
}
