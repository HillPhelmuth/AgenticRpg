using System.Net;
using System.Net.Http.Json;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class HttpAndHubTests
{
    [Fact]
    public async Task DirectNarrativeRoutesRejectGmContentAndForgedCharacterOwnership()
    {
        await using var app = SeededApp();
        using var alice = app.Client("alice");
        using var bob = app.Client("bob");
        using var anonymous = app.Client();
        var entries = await alice.GetFromJsonAsync<List<Narrative>>("/api/narratives/campaign/campaign");
        Assert.Equal(new[] { "public", "a-secret" }, entries!.Select(n => n.Id));
        var characterEntries = await bob.GetFromJsonAsync<List<Narrative>>("/api/narratives/campaign/campaign/character/b");
        Assert.Equal(new[] { "public", "b-secret" }, characterEntries!.Select(n => n.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/narratives/campaign/campaign/character/b")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/narratives/gm")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/narratives/b-secret")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/narratives/public")).StatusCode);
        var forged = TestRuntime.Character("b", "alice");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync("/api/characters/b", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/characters/b", forged)).StatusCode);
        var createdResponse = await alice.PostAsJsonAsync("/api/characters", forged);
        var created = (await createdResponse.Content.ReadFromJsonAsync<Character>())!;
        Assert.NotEqual("b", created.Id);
        Assert.Equal("bob", app.Fakes.CharacterRecords["b"].PlayerId);
    }

    [Fact]
    public async Task OwnerWithoutCharacterReceivesOnlyPublicContent()
    {
        await using var app = SeededApp();
        app.Fakes.CharacterRecords.Remove("a");
        using var owner = app.Client("alice");
        var state = await owner.GetFromJsonAsync<GameState>("/api/gamestate/campaign");
        Assert.Equal("public", Assert.Single(state!.RecentNarratives).Id);
        var narratives = await owner.GetFromJsonAsync<List<Narrative>>("/api/narratives/campaign/campaign");
        Assert.Equal("public", Assert.Single(narratives!).Id);
    }

    [Fact]
    public async Task HttpStateIsFilteredAndSessionAccessRejectsImpersonation()
    {
        await using var app = SeededApp();
        using var alice = app.Client("alice");
        using var bob = app.Client("bob");
        using var anonymous = app.Client();
        using var stranger = app.Client("stranger");
        var aliceState = await alice.GetFromJsonAsync<GameState>("/api/gamestate/campaign");
        var bobState = await bob.GetFromJsonAsync<GameState>("/api/gamestate/campaign");
        Assert.Equal(new[] { "public", "a-secret" }, aliceState!.RecentNarratives.Select(n => n.Id));
        Assert.Equal(new[] { "public", "b-secret" }, bobState!.RecentNarratives.Select(n => n.Id));
        Assert.Empty(aliceState.Metadata);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/gamestate/campaign")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/gamestate/campaign")).StatusCode);
        var response = await alice.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
            { PlayerId = "bob", SessionType = SessionType.CharacterCreation });
        var session = await response.Content.ReadFromJsonAsync<SessionState>();
        Assert.Equal("alice", session!.PlayerId);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/sessions/" + session.SessionId)).StatusCode);
        var forbiddenCompletion = await bob.PostAsync($"/api/sessions/{session.SessionId}/complete", null);
        Assert.True(forbiddenCompletion.StatusCode == HttpStatusCode.Forbidden, $"{forbiddenCompletion.StatusCode}: {await forbiddenCompletion.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync("/api/sessions/" + session.SessionId)).StatusCode);
        Assert.Equal(4, app.Fakes.State.RecentNarratives.Count);
        Assert.Single(app.Fakes.State.Metadata);
    }

    [Fact]
    public async Task HubRetrievalRecoveryAndPrivateEventsUseValidatedCharacter()
    {
        await using var app = SeededApp();
        await using var alice = Connection(app, "alice");
        await using var bob = Connection(app, "bob");
        var aliceEvents = new List<Narrative>();
        var bobEvents = new List<Narrative>();
        using var aSubscription = alice.On<string, Narrative>("NarrativeAdded", (_, n) => { lock (aliceEvents) aliceEvents.Add(n); });
        using var bSubscription = bob.On<string, Narrative>("NarrativeAdded", (_, n) => { lock (bobEvents) bobEvents.Add(n); });
        await Task.WhenAll(alice.StartAsync(), bob.StartAsync());
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync<ScopeRecoverySnapshot>("JoinCampaign", "campaign", "bob", "b", "Bob", "forged"));
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync<ScopeRecoverySnapshot>("JoinCampaign", "campaign", "alice", "b", "Bob", "forged"));
        var a = await Join(alice, "alice", "a", "one");
        var b = await Join(bob, "bob", "b", "two");
        Assert.Equal(new[] { "public", "a-secret" }, a.GameState!.RecentNarratives.Select(n => n.Id));
        Assert.Equal(new[] { "public", "b-secret" }, b.GameState!.RecentNarratives.Select(n => n.Id));
        var retrieved = await alice.InvokeAsync<GameState>("GetGameState", "campaign");
        Assert.DoesNotContain(retrieved.RecentNarratives, n => n.Id == "gm" || n.Id == "b-secret");
        var broadcaster = app.Services.GetRequiredService<PlayerStateBroadcaster>();
        foreach (var n in app.Fakes.State.RecentNarratives) await broadcaster.NarrativeAsync("campaign", n);
        await TestRuntime.UntilAsync(() => { lock (aliceEvents) lock (bobEvents) return aliceEvents.Count == 2 && bobEvents.Count == 2; });
        Assert.Equal(new[] { "public", "a-secret" }, aliceEvents.Select(n => n.Id));
        Assert.Equal(new[] { "public", "b-secret" }, bobEvents.Select(n => n.Id));
    }

    [Fact]
    public async Task NavigationLeaseAndMultipleTabsPreserveMembershipAndWithdrawalPersistsOnce()
    {
        await using var app = SeededApp();
        await using var tab1 = Connection(app, "alice");
        await using var tab2 = Connection(app, "alice");
        await Task.WhenAll(tab1.StartAsync(), tab2.StartAsync());
        await Task.WhenAll(Join(tab1, "alice", "a", "lobby"), Join(tab2, "alice", "a", "other"));
        await Join(tab1, "alice", "a", "game");
        await tab1.InvokeAsync("DetachScope", "campaign", "lobby");
        Assert.Equal(2, app.Fakes.Connections.InScope("campaign").Length);
        await tab2.StopAsync();
        await TestRuntime.UntilAsync(() => app.Fakes.Connections.InScope("campaign").Length == 1);
        Assert.Contains("alice", app.Fakes.State.Campaign.ActiveAndConnectedPlayerIds);
        await tab1.InvokeAsync("DetachScope", "campaign", "game");
        Assert.Contains("alice", app.Fakes.State.Campaign.PlayerIds);
        Assert.Equal("campaign", app.Fakes.CharacterRecords["a"].CampaignId);
        Assert.DoesNotContain("alice", app.Fakes.State.Campaign.ActiveAndConnectedPlayerIds);
        await Join(tab1, "alice", "a", "again");
        await tab1.InvokeAsync("LeaveCampaign", "campaign", "alice", "a");
        await tab1.InvokeAsync("LeaveCampaign", "campaign", "alice", "a");
        Assert.Empty(app.Fakes.CharacterRecords["a"].CampaignId);
        Assert.DoesNotContain("alice", app.Fakes.State.Campaign.PlayerIds);
        app.Fakes.Characters.Verify(r => r.UpdateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconnectDuringWorkConvergesWithoutRepeatingActions()
    {
        await using var app = SeededApp();
        await using var tab = Connection(app, "alice");
        await tab.StartAsync();
        await Join(tab, "alice", "a", "initial");
        var ack = await tab.InvokeAsync<RuntimeMessage>("SendMessage", "campaign", "alice", "Open door", AgentType.None, "action");
        var work = Assert.Single(app.Fakes.Processor.Requests);
        await work.StreamCallback!(new() { Token = "Opening", AgentType = "GameMaster" });
        await tab.StopAsync();
        await work.StreamCallback!(new() { Token = " the door", AgentType = "GameMaster" });
        await tab.StartAsync();
        var recovery = await Join(tab, "alice", "a", "reconnected");
        Assert.Equal("Opening the door", Assert.Single(recovery.Messages).Content);
        Assert.False(recovery.Messages[0].IsComplete);
        var retry = await tab.InvokeAsync<RuntimeMessage>("SendMessage", "campaign", "alice", "Open door", AgentType.None, "action");
        Assert.Equal(ack.Id, retry.Id);
        Assert.Single(app.Fakes.Processor.Requests);
        app.Fakes.Processor.Complete("action");
        var runtime = app.Services.GetRequiredService<GameRuntimeCoordinator>();
        await TestRuntime.UntilAsync(() => !runtime.HasPending("campaign"));
        await tab.StopAsync();
        await tab.StartAsync();
        recovery = await Join(tab, "alice", "a", "completed");
        Assert.True(Assert.Single(recovery.Messages).IsComplete);
        Assert.Single(app.Fakes.Processor.Requests);
    }

    [Fact]
    public async Task DiceRecoveryReconcilesWindowIdsAndAcceptsEachResultOnce()
    {
        await using var app = SeededApp();
        await using var tab = Connection(app, "alice");
        await tab.StartAsync();
        await Join(tab, "alice", "a", "initial");
        var dice = app.Services.GetRequiredService<RollDiceHubService>();
        var parameters = new RollDiceParameters();
        parameters.Set("PlayerId", "alice");
        var waiting = dice.RequestDiceRoll("campaign", parameters, numberOfRollWindows: 2);
        var request = Assert.Single(dice.GetPending("campaign", "alice"));
        Assert.Empty(dice.GetPending("campaign", "bob"));
        var first = new RollDiceResults(true) { SessionId = "campaign", RequestId = request.WindowRequestIds![0] };
        await tab.InvokeAsync("SubmitDiceRollResult", new RollDiceResultsList { Results = [first, first] });
        await tab.StopAsync();
        await tab.StartAsync();
        var recovery = await Join(tab, "alice", "a", "retry");
        var remaining = Assert.Single(recovery.PendingDice);
        Assert.Equal(request.RequestId, remaining.RequestId);
        Assert.Single(remaining.WindowRequestIds!);
        Assert.Equal(request.WindowRequestIds[1], remaining.WindowRequestIds![0]);
        var second = new RollDiceResults(true) { SessionId = "campaign", RequestId = remaining.WindowRequestIds[0] };
        await tab.InvokeAsync("SubmitDiceRollResult", new RollDiceResultsList { Results = [second, second] });
        Assert.Equal(2, (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Count);
        Assert.Empty(dice.GetPending("campaign", "alice"));
    }

    [Fact]
    public async Task CampaignSaveAndCompletedSessionRecoveryAreIdempotent()
    {
        await using var app = SeededApp();
        using var client = app.Client("alice");
        var response = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
            { SessionType = SessionType.CampaignSetup, WorldId = "world" });
        var session = (await response.Content.ReadFromJsonAsync<SessionState>())!;
        var draft = await client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/campaign-draft", new Campaign { Name = "New campaign", OwnerId = "bob" });
        Assert.True(draft.IsSuccessStatusCode);
        var saves = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync($"/api/sessions/{session.SessionId}/finalize-campaign", null)));
        Assert.All(saves, r => Assert.True(r.IsSuccessStatusCode));
        app.Fakes.Campaigns.Verify(r => r.CreateAsync(It.Is<Campaign>(c => c.OwnerId == "alice" && c.WorldId == "world"), It.IsAny<CancellationToken>()), Times.Once);
        await using var tab = Connection(app, "alice");
        await tab.StartAsync();
        var recovery = await tab.InvokeAsync<ScopeRecoverySnapshot>("JoinSession", session.SessionId, "alice", "Creator", "recover");
        Assert.True(recovery.SessionState!.IsCompleted);
        Assert.NotEmpty(recovery.SessionState.ResultEntityId!);
    }

    [Fact]
    public async Task StructuredActionAndCancellationUseAuthenticatedActorAndPreserveRequestIdentity()
    {
        await using var app = SeededApp();
        await using var alice = Connection(app, "alice");
        await using var bob = Connection(app, "bob");
        await alice.StartAsync(); await bob.StartAsync();
        await Join(alice, "alice", "a", "a"); await Join(bob, "bob", "b", "b");
        var accepted = await alice.InvokeAsync<RuntimeMessage>("SendAction", "campaign", "alice", new PlayerActionIntent { Kind = PlayerActionKind.Rest }, "rest-intent");
        var request = app.Fakes.Processor.Requests.Single();
        Assert.Equal("a", request.CharacterId);
        Assert.Equal(PlayerActionKind.Rest, request.ActionIntent!.Kind);
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync<RuntimeMessage>("CancelRequest", "campaign", "rest-intent"));
        await alice.InvokeAsync<RuntimeMessage>("CancelRequest", "campaign", "rest-intent");
        await TestRuntime.UntilAsync(() => !app.Fakes.Runtime.HasPending("campaign"));
        var duplicate = await alice.InvokeAsync<RuntimeMessage>("SendAction", "campaign", "alice", new PlayerActionIntent { Kind = PlayerActionKind.Rest }, "rest-intent");
        Assert.Equal(accepted.Id, duplicate.Id);
        Assert.Single(app.Fakes.Processor.Requests);
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync<RuntimeMessage>("SendAction", "campaign", "bob", new PlayerActionIntent { Kind = PlayerActionKind.Rest }, "forged"));
    }

    [Fact]
    public async Task QuickStarterAndJourneyPersistInDraftAndUnknownStartersAreRejected()
    {
        await using var app = SeededApp();
        using var client = app.Client("alice");
        var response = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { SessionType = SessionType.WorldBuilding, QuickStarter = "Haunted frontier", StartNewAdventure = true });
        var session = (await response.Content.ReadFromJsonAsync<SessionState>())!;
        var recovered = await client.GetFromJsonAsync<SessionState>($"/api/sessions/{session.SessionId}");
        Assert.Equal("Haunted frontier", recovered!.Context.QuickStarter);
        Assert.True(recovered.Context.StartNewAdventure);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { SessionType = SessionType.CharacterCreation, QuickStarter = "Forged starter" })).StatusCode);
    }

    private static PlayerAppFactory SeededApp()
    {
        var app = new PlayerAppFactory();
        app.Fakes.CharacterRecords["a"] = TestRuntime.Character();
        app.Fakes.CharacterRecords["b"] = TestRuntime.Character("b", "bob");
        app.Fakes.State.Characters = app.Fakes.CharacterRecords.Values.ToList();
        app.Fakes.State.RecentNarratives = TestRuntime.Narratives();
        app.Fakes.State.Metadata["secret"] = "Internal";
        app.Fakes.WorldRecords["world"] = new() { Id = "world", Name = "Test world", CreatorId = "alice" };
        return app;
    }
    private static HubConnection Connection(PlayerAppFactory app, string player) => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/game", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
            options.Headers["Cookie"] = "PlaytestPlayer=" + player;
        }).Build();
    private static Task<ScopeRecoverySnapshot> Join(HubConnection tab, string player, string character, string lease) =>
        tab.InvokeAsync<ScopeRecoverySnapshot>("JoinCampaign", "campaign", player, character, player, lease);
}
