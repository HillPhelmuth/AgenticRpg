using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class CompletionAndFilteringTests
{
    [Fact]
    public async Task ConcurrentCharacterFinalizersSaveOnceAndPrepareEquipmentOnce()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = TestRuntime.Character();
        session.Context.CampaignId = "campaign";
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => f.Completion.FinalizeCharacterAsync(session.SessionId)));
        Assert.Single(results.Distinct());
        f.Characters.Verify(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once);
        var character = session.Context.DraftCharacter;
        Assert.NotNull(character.Equipment.MainHand);
        Assert.NotNull(character.Equipment.Armor);
        Assert.Equal(character.Inventory.Count, character.Inventory.DistinctBy(i => i.Id).Count());
        Assert.Equal("campaign", character.CampaignId);
        Assert.True(session.IsCompleted);
        Assert.Single(f.Events.Where(e => e.Method == "SessionCompleted"));
    }

    [Fact]
    public async Task IncompleteCharacterExplainsRequirementsAndLeavesDraft()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.CharacterCreation, "alice");
        session.Context.DraftCharacter = new() { Attributes = [], Level = 0 };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.FinalizeCharacterAsync(session.SessionId));
        foreach (var required in new[] { "name", "race", "class", "attributes", "hit points" }) Assert.Contains(required, error.Message);
        Assert.False(session.IsCompleted);
        Assert.NotNull(session.Context.DraftCharacter);
        f.Characters.Verify(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorldEditPreservesIdAndCreationTimestamp()
    {
        var f = new TestRuntime();
        var original = new World { Id = "old", Name = "Original", CreatedAt = DateTime.UtcNow.AddYears(-1), CreatorId = "alice" };
        f.WorldRecords[original.Id] = original;
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice", original);
        session.Context.DraftWorld = new() { Name = "Updated" };
        var id = await f.Completion.FinalizeWorldAsync(session.SessionId);
        Assert.Equal("old", id);
        Assert.Equal(original.CreatedAt, f.WorldRecords[id].CreatedAt);
        f.Worlds.Verify(r => r.UpdateAsync(It.IsAny<World>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Worlds.Verify(r => r.CreateAsync(It.IsAny<World>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewWorldRequiresOnlyWorldNameAndRepositoryFailureCanBeRetried()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        session.Context.DraftWorld = new() { Name = "New" };
        f.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.FinalizeWorldAsync(session.SessionId));
        Assert.False(session.IsCompleted);
        Assert.Equal("New", session.Context.DraftWorld.Name);
        Assert.DoesNotContain(f.Events, e => e.Method == "SessionCompleted");
        f.FailWrites = false;
        var id = await f.Completion.FinalizeWorldAsync(session.SessionId);
        Assert.Equal(id, await f.Completion.FinalizeWorldAsync(session.SessionId));
        Assert.Single(f.WorldRecords);
    }

    [Fact]
    public async Task DeliveryFailureAfterPersistenceDoesNotCreateAnotherEntity()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        session.Context.DraftWorld = new() { Name = "New" };
        f.Clients.Setup(c => c.Group(It.IsAny<string>())).Throws(new IOException("Disconnected"));
        var id = await f.Completion.FinalizeWorldAsync(session.SessionId);
        Assert.Equal(id, await f.Completion.FinalizeWorldAsync(session.SessionId));
        f.Worlds.Verify(r => r.CreateAsync(It.IsAny<World>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("a", "a-secret")]
    [InlineData("b", "b-secret")]
    [InlineData(null, null)]
    public void ProjectionClearsSecretsWithoutMutatingInternalState(string? character, string? secret)
    {
        var f = new TestRuntime();
        f.State.Metadata["Internal"] = "Hidden";
        f.State.RecentNarratives = TestRuntime.Narratives();
        f.State.RecentNarratives.Add(f.State.RecentNarratives[0]);
        var projected = f.Projector.Project(f.State, character);
        Assert.NotSame(f.State, projected);
        Assert.Empty(projected.Metadata);
        Assert.Equal(secret is null ? ["public"] : new[] { "public", secret }, projected.RecentNarratives.Select(n => n.Id));
        Assert.Equal(5, f.State.RecentNarratives.Count);
        Assert.Single(f.State.Metadata);
    }

    [Fact]
    public async Task PrivateNarrativeBroadcastGoesToEveryControllingTabAndNeverToGmOwner()
    {
        var f = new TestRuntime();
        f.Connections.Attach("alice-tab1", new("campaign", "alice", "a", "Alice", false, "1"));
        f.Connections.Attach("alice-tab2", new("campaign", "alice", "a", "Alice", false, "2"));
        f.Connections.Attach("bob-tab", new("campaign", "bob", "b", "Bob", false, "3"));
        f.State.RecentNarratives = TestRuntime.Narratives();
        foreach (var narrative in f.State.RecentNarratives) await f.Broadcaster.NarrativeAsync("campaign", narrative);
        await f.Broadcaster.StateAsync(f.State);
        foreach (var tab in new[] { "alice-tab1", "alice-tab2" })
        {
            Assert.Equal(new[] { "public", "a-secret" }, f.Events.Where(e => e.Target == tab && e.Method == "NarrativeAdded").Select(e => ((Narrative)e.Args[1]!).Id));
            Assert.DoesNotContain(((GameState)f.Events.Single(e => e.Target == tab && e.Method == "StateUpdated").Args[0]!).RecentNarratives, n => n.Id == "gm" || n.Id == "b-secret");
        }
        Assert.Equal(new[] { "public", "b-secret" }, f.Events.Where(e => e.Target == "bob-tab" && e.Method == "NarrativeAdded").Select(e => ((Narrative)e.Args[1]!).Id));
    }

    [Fact]
    public void OldScopeLeaseCannotDetachANewerPageOrOtherTabs()
    {
        var registry = new GameConnectionRegistry();
        registry.Attach("tab1", new("campaign", "alice", "a", "Alice", false, "lobby"));
        registry.Attach("tab2", new("campaign", "alice", "a", "Alice", false, "other"));
        registry.Attach("tab1", new("campaign", "alice", "a", "Alice", false, "game"));
        Assert.Null(registry.Detach("tab1", "lobby"));
        Assert.NotNull(registry.Detach("tab2", "other"));
        Assert.True(registry.HasPlayer("campaign", "alice"));
        registry.Detach("tab1", "game");
        Assert.False(registry.HasPlayer("campaign", "alice"));
    }
}
