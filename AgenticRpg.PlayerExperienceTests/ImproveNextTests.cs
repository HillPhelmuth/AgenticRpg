using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Components.GameComponents;
using AgenticRpg.Core;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticRpg.PlayerExperienceTests;

public class ImproveNextTests
{
    [Fact]
    public async Task QueuedCancellationIsTerminalIdempotentAndDoesNotRequireReview()
    {
        var f = new TestRuntime();
        await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Wait", AgentType.None, "cancel", false);
        await TestRuntime.UntilAsync(() => !f.Processor.Requests.IsEmpty);
        await f.Runtime.CancelAsync("campaign", "alice", "cancel");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        var result = await f.Runtime.CancelAsync("campaign", "alice", "cancel");
        Assert.Equal(MessageProcessingStatus.Cancelled, result.Status);
        Assert.False(await f.Runtime.RequiresReviewAsync("campaign", "alice"));
        Assert.Equal(result.Status, (await f.Recovery.FindRequestAsync("campaign", "alice", "cancel"))!.Read<RuntimeMessage>().Status);
    }

    [Fact]
    public async Task ActiveCancellationRetainsPartialTextAndEffectsAcrossRecoveryAndRequiresReview()
    {
        var f = new TestRuntime();
        f.State.Characters = [TestRuntime.Character()];
        await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "active", false);
        await TestRuntime.UntilAsync(() => !f.Processor.Requests.IsEmpty);
        var request = f.Processor.Requests.Single();
        await request.StatusCallback!(new() { Status = MessageProcessingStatus.Processing });
        await request.StreamCallback!(new() { Token = "Partial narration", AgentType = "GameMaster" });
        f.State.Characters[0].CurrentHP = 7;
        await f.Runtime.CancelAsync("campaign", "alice", "active");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        var result = f.Runtime.Find("campaign", "alice", "active")!;
        Assert.Equal(MessageProcessingStatus.Interrupted, result.Status);
        Assert.Contains("Partial narration", result.Content);
        Assert.Equal(7, f.State.Characters[0].CurrentHP);
        Assert.True(await f.Runtime.RequiresReviewAsync("campaign", "alice"));
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Next", AgentType.None, "new", false));
        var restored = new GameRuntimeCoordinator(f.Processor, f.Hub.Object, f.States.Object, f.Sessions, f.Broadcaster,
            NullLogger<GameRuntimeCoordinator>.Instance, f.Locks, recovery: f.Recovery);
        await restored.RecoverAsync("campaign");
        Assert.True(await restored.RequiresReviewAsync("campaign", "alice"));
        await restored.ReviewAsync("campaign", "alice");
        Assert.False(await restored.RequiresReviewAsync("campaign", "alice"));
        Assert.Single(f.Processor.Requests);
    }

    [Fact]
    public async Task AnotherPlayerCannotCancelAndCompletedWorkWinsLateCancellation()
    {
        var f = new TestRuntime();
        await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "owned", false);
        await TestRuntime.UntilAsync(() => !f.Processor.Requests.IsEmpty);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => f.Runtime.CancelAsync("campaign", "bob", "owned"));
        f.Processor.Complete("owned");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        Assert.Equal(MessageProcessingStatus.Completed, (await f.Runtime.CancelAsync("campaign", "alice", "owned")).Status);
    }

    [Fact]
    public async Task CompletionCheckpointAndCancellationChooseOneTerminalOutcome()
    {
        var f = new TestRuntime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Threads.Setup(t => t.CommitScopeAsync("campaign")).Returns(async () =>
        {
            entered.SetResult();
            await finish.Task;
        });
        var runtime = new GameRuntimeCoordinator(f.Processor, f.Hub.Object, f.States.Object, f.Sessions, f.Broadcaster,
            NullLogger<GameRuntimeCoordinator>.Instance, f.Locks, recovery: f.Recovery, threads: f.Threads.Object);
        await runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Act", AgentType.None, "race", false);
        await TestRuntime.UntilAsync(() => !f.Processor.Requests.IsEmpty);
        f.Processor.Complete("race");
        await entered.Task;
        var cancel = runtime.CancelAsync("campaign", "alice", "race");
        Assert.False(cancel.IsCompleted);
        finish.SetResult();
        Assert.Equal(MessageProcessingStatus.Completed, (await cancel).Status);
        await TestRuntime.UntilAsync(() => !runtime.HasPending("campaign"));
        Assert.False(await runtime.RequiresReviewAsync("campaign", "alice"));
        f.Threads.Verify(t => t.DiscardScope("campaign"), Moq.Times.Never);
    }

    [Fact]
    public async Task CancellationWaitsForAnInFlightWriteBeforeReleasingTheRequest()
    {
        var f = new TestRuntime();
        var processor = new WriteBoundaryProcessor();
        var runtime = new GameRuntimeCoordinator(processor, f.Hub.Object, f.States.Object, f.Sessions, f.Broadcaster,
            NullLogger<GameRuntimeCoordinator>.Instance, f.Locks, recovery: f.Recovery);
        await runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Write", AgentType.None, "write", false);
        await processor.Entered.Task;
        await runtime.CancelAsync("campaign", "alice", "write");
        Assert.True(runtime.HasPending("campaign"));
        Assert.True(f.Locks.HasActiveRequests("campaign"));
        processor.FinishWrite.SetResult();
        await TestRuntime.UntilAsync(() => !runtime.HasPending("campaign") && !f.Locks.HasActiveRequests("campaign"));
        Assert.True(processor.EffectApplied);
        Assert.True(await runtime.RequiresReviewAsync("campaign", "alice"));
    }

    private sealed class WriteBoundaryProcessor : IGameMessageProcessor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool EffectApplied { get; private set; }
        public async Task<AgentResponse> ProcessAsync(PlayerMessageRequest request, bool isSession)
        {
            await request.StatusCallback!(new() { Status = MessageProcessingStatus.Processing });
            Entered.SetResult();
            await FinishWrite.Task;
            EffectApplied = true;
            request.CancellationToken.ThrowIfCancellationRequested();
            return new() { Success = true };
        }
    }

    [Fact]
    public async Task QueueSkipsCancelledWorkWithoutStartingItsProcessor()
    {
        var running = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new List<string>();
        var queue = new CampaignMessageQueue(request => { started.Add(request.Message); return running.Task; }, NullLogger.Instance);
        var first = queue.EnqueueAsync(new() { CampaignId = "c", PlayerId = "a", Message = "First" }, 0);
        using var cancel = new CancellationTokenSource();
        var second = queue.EnqueueAsync(new() { CampaignId = "c", PlayerId = "b", Message = "Second", CancellationToken = cancel.Token }, 0);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
        running.SetResult(new() { Success = true });
        await first;
        Assert.Equal(new[] { "First" }, started);
    }

    [Fact]
    public async Task DiceCancellationRemovesPendingWindowsAndLateResultsAreHarmless()
    {
        var f = new TestRuntime();
        using var cancel = new CancellationTokenSource();
        using var execution = RequestExecution.Enter(new() { CampaignId = "campaign", PlayerId = "alice", Message = "Roll", CancellationToken = cancel.Token });
        var dice = new RollDiceHubService(f.Hub.Object);
        var roll = dice.RequestDiceRoll("campaign");
        Assert.Single(dice.GetPending("campaign", "alice"));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await roll);
        Assert.Empty(dice.GetPending("campaign", "alice"));
        Assert.Empty(dice.DieRollReferences);
        Assert.Contains(f.Events, e => e.Method == "DiceRequestClosed");
    }

    [Fact]
    public void StructuredActionUsesCanonicalCostsAndRejectsStaleSelections()
    {
        var character = TestRuntime.Character();
        character.KnownSpells = [new() { Id = "spell", Name = "Bolt", Level = 2 }];
        character.CurrentMP = 3;
        var state = new GameState { Campaign = new() { Id = "campaign" }, Characters = [character] };
        var intent = new PlayerActionIntent { Kind = PlayerActionKind.Cast, SelectionId = "spell", TargetId = character.Id };
        Assert.Contains("3 MP", PlayerActionRules.Describe(state, character, intent));
        character.CurrentMP = 2;
        Assert.Throws<InvalidOperationException>(() => PlayerActionRules.Describe(state, character, intent));
        Assert.Throws<InvalidOperationException>(() => PlayerActionRules.Describe(state, character, intent with { SelectionId = "someone-elses-spell" }));
        Assert.False(PlayerActionRules.CanEquip(new ArmorItem(), EquipSlot.MainHand));
    }

    [Fact]
    public async Task ActionIntentSurvivesPersistenceAndDuplicateRequestDoesNotRunAgain()
    {
        var f = new TestRuntime();
        var intent = new PlayerActionIntent { Kind = PlayerActionKind.Rest };
        await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Rest", AgentType.GameMaster, "intent", false, intent);
        await TestRuntime.UntilAsync(() => !f.Processor.Requests.IsEmpty);
        Assert.Equal(intent, f.Processor.Requests.Single().ActionIntent);
        f.Processor.Complete("intent");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        var duplicate = await f.Runtime.SubmitAsync("campaign", "alice", "a", "Alice", "Rest", AgentType.GameMaster, "intent", false, intent);
        Assert.Equal(intent, duplicate.ActionIntent);
        Assert.Single(f.Processor.Requests);
    }

    [Fact]
    public async Task ItemUseAppliesServerEffectsAndConsumesOnlyAnAvailableItem()
    {
        var f = new TestRuntime();
        var actor = TestRuntime.Character(); actor.CurrentHP = 5;
        actor.Inventory = [new() { Id = "potion", Name = "Potion", ItemType = ItemType.Consumable, Quantity = 1 }];
        f.State.Characters = [actor];
        var tools = new AgenticRpg.Core.Agents.Tools.GameMasterTools(f.States.Object, f.NarrativesRepository.Object);
        await tools.UseInventoryItem("campaign", "a", "potion", "Restore two HP", hpChange: 2, consume: true);
        Assert.Equal(7, actor.CurrentHP);
        Assert.Equal(0, actor.Inventory[0].Quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.UseInventoryItem("campaign", "a", "potion", "Try again", hpChange: 2, consume: true));
        Assert.Equal(7, actor.CurrentHP);
    }

    [Fact]
    public async Task JournalDeduplicatesPrimaryQuestAndFiltersSecretDiscoveries()
    {
        await using var context = new BunitContext();
        var quest = new Quest { Id = "q", Name = "Find the gate", Objectives = ["Find key", "Open gate"], CompletedObjectives = new() { ["Find key"] = true } };
        var state = new GameState { Campaign = new() { PrimaryQuest = quest }, World = new() { SideQuests = [quest] }, RecentNarratives = TestRuntime.Narratives() };
        var journal = context.Render<AdventureJournal>(p => p.Add(c => c.State, state).Add(c => c.CharacterId, "a"));
        Assert.Single(journal.FindAll("h4"));
        Assert.Contains("Find key", journal.Markup);
        Assert.DoesNotContain("Hidden villain", journal.Markup);
        Assert.DoesNotContain("Bob&#x27;s clue", journal.Markup);
        var overview = context.Render<AdventureOverview>(p => p.Add(c => c.State, state).Add(c => c.Character, TestRuntime.Character()));
        Assert.Contains("Open gate", overview.Markup);
        Assert.Contains("HP", overview.Markup);
    }

    [Fact]
    public async Task SuggestionsSurviveNotificationsButExpireOnTurnChangeOrNewSubmission()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var now = DateTime.UtcNow;
        var messages = new List<ChatMessage> { new() { Id = "response", Content = "Ready", Status = MessageProcessingStatus.Completed, Timestamp = now, SuggestionContext = "turn-1", SuggestedActions = ["Open door"] }, new() { Id = "notice", Content = "Someone joined", Timestamp = now.AddSeconds(1) } };
        var chat = context.Render<ChatView>(p => p.Add(c => c.Messages, messages).Add(c => c.SuggestionContext, "turn-1"));
        Assert.Single(chat.FindAll(".suggested-action-btn"));
        messages.Add(new() { Id = "rejected", IsUser = true, WasRejected = true, Status = MessageProcessingStatus.Failed, Timestamp = now.AddSeconds(1) });
        chat.Render();
        Assert.Single(chat.FindAll(".suggested-action-btn"));
        chat.Render(p => p.Add(c => c.SuggestionContext, "turn-2"));
        Assert.Empty(chat.FindAll(".suggested-action-btn"));
        messages.Add(new() { Id = "next", IsUser = true, IsAuthoritative = true, Content = "New action", Timestamp = now.AddSeconds(2) });
        chat.Render(p => p.Add(c => c.SuggestionContext, "turn-1"));
        Assert.Empty(chat.FindAll(".suggested-action-btn"));
    }

    [Theory]
    [InlineData(true, "Warrior")]
    [InlineData(true, "Rogue")]
    [InlineData(true, "Wizard")]
    [InlineData(false, "Classic fantasy")]
    [InlineData(false, "Haunted frontier")]
    [InlineData(false, "Arcane city")]
    public void QuickCreationPromptsKeepReviewAndInsultingComedy(bool character, string starter)
    {
        var prompt = StarterCreation.Prompt(character, starter);
        Assert.Contains(starter, prompt);
        Assert.Contains("insulting comedy", prompt);
        Assert.Contains("Do not finalize or save", prompt);
    }
}
