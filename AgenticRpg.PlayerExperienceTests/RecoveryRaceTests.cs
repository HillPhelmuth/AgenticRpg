using AgenticRpg.Core;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Services;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class RecoveryRaceTests
{
    [Fact]
    public async Task DeliveryFailureAfterSuccessfulProcessingDoesNotMarkActionFailed()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        f.Runtime.Submit(session.SessionId, "alice", "", "Alice", "Build", AgentType.WorldBuilder, "work", true);
        f.Clients.Setup(c => c.Group(It.IsAny<string>())).Throws(new IOException("Disconnected"));
        f.Processor.Complete("work");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending(session.SessionId));
        Assert.Equal(MessageProcessingStatus.Completed, Assert.Single(f.Runtime.Snapshot(session.SessionId)).Status);
    }

    [Fact]
    public async Task CreationQueueRunsOnlyOneRequestAtATime()
    {
        var first = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = new List<string>();
        var queue = new CampaignMessageQueue(request =>
        {
            lock (starts) starts.Add(request.ClientMessageId!);
            return request.ClientMessageId == "first" ? first.Task : second.Task;
        }, NullLogger.Instance);
        PlayerMessageRequest Request(string id) => new() { CampaignId = "session", PlayerId = "alice", Message = id, ClientMessageId = id };
        var task1 = queue.EnqueueAsync(Request("first"), 0);
        var task2 = queue.EnqueueAsync(Request("second"), 0);
        Assert.Equal(new[] { "first" }, starts);
        first.SetResult(new() { Success = true });
        await task1;
        await TestRuntime.UntilAsync(() => starts.Count == 2);
        second.SetResult(new() { Success = true });
        await task2;
        Assert.Equal(new[] { "first", "second" }, starts);
    }

    [Fact]
    public async Task ManualSaveCannotRaceActiveGenerationButAiCanFinalizeItsOwnRequest()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        session.Context.DraftWorld = new() { Name = "Draft" };
        f.Runtime.Submit(session.SessionId, "alice", "", "Alice", "Build", AgentType.WorldBuilder, "work", true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Completion.FinalizeWorldAsync(session.SessionId, requireIdle: true));
        var aiResult = await f.Completion.FinalizeWorldAsync(session.SessionId);
        Assert.Equal(aiResult, await f.Completion.FinalizeWorldAsync(session.SessionId, requireIdle: true));
        f.Processor.Complete("work");
        await TestRuntime.UntilAsync(() => !f.Locks.HasActiveRequests(session.SessionId));
        Assert.Single(f.WorldRecords);
    }

    [Fact]
    public async Task ActiveSessionSurvivesExpirationAndCompletedRecoveryGetsAnotherDay()
    {
        var f = new TestRuntime();
        var session = await f.Sessions.CreateSessionAsync(SessionType.WorldBuilding, "alice");
        session.Context.DraftWorld = new() { Name = "Draft" };
        f.Runtime.Submit(session.SessionId, "alice", "", "Alice", "Build", AgentType.WorldBuilder, "work", true);
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        Assert.NotNull(await f.Sessions.GetSessionStateAsync(session.SessionId));
        await f.Completion.FinalizeWorldAsync(session.SessionId);
        f.Processor.Complete("work");
        await TestRuntime.UntilAsync(() => !f.Locks.HasActiveRequests(session.SessionId));
        Assert.True((await f.Sessions.GetSessionStateAsync(session.SessionId))!.ExpiresAt > DateTime.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task UndeliveredDiceRequestRemainsRecoverableInsteadOfFailingTheAction()
    {
        var f = new TestRuntime();
        f.Clients.Setup(c => c.Group(It.IsAny<string>())).Throws(new IOException("Delivery failed"));
        var dice = new RollDiceHubService(f.Hub.Object);
        var waiting = dice.RequestDiceRoll("campaign");
        var pending = Assert.Single(dice.GetPending("campaign", "alice"));
        Assert.False(waiting.IsCompleted);
        dice.Submit(new RollDiceResults(true) { SessionId = "campaign", RequestId = pending.WindowRequestIds![0] }, "alice");
        Assert.Single(await waiting);
    }

    [Fact]
    public async Task OldCompletedResponsesExpireWhileActiveWorkRemains()
    {
        var f = new TestRuntime();
        var time = new Clock();
        var runtime = new GameRuntimeCoordinator(f.Processor, f.Hub.Object, f.States.Object, f.Sessions,
            f.Broadcaster, NullLogger<GameRuntimeCoordinator>.Instance, f.Locks, time);
        runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "old", false);
        f.Processor.Complete("old");
        await TestRuntime.UntilAsync(() => !f.Locks.HasActiveRequests("campaign"));
        runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "active", false);
        time.Now += TimeSpan.FromHours(25);
        Assert.Equal("active", Assert.Single(runtime.Snapshot("campaign")).ClientMessageId);
        f.Processor.Complete("active");
        await TestRuntime.UntilAsync(() => !f.Locks.HasActiveRequests("campaign"));
        time.Now += TimeSpan.FromHours(25);
        Assert.Empty(runtime.Snapshot("campaign"));
        Assert.True(runtime.HasAcceptedRequests("campaign"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
