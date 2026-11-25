using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Microsoft.AspNetCore.SignalR;

namespace AgenticRpg.PlayerExperienceTests;

public class StreamingTests
{
    [Fact]
    public void InterleavedMessagesAndNotificationsNeverReceiveAnotherStreamsText()
    {
        var reducer = new RuntimeChatState();
        var messages = new List<ChatMessage>();
        var a = Response("a", "alice", 1, "First");
        reducer.Apply(messages, a, "alice");
        messages.Add(new() { Id = "turn", Content = "Bob's turn" });
        reducer.Apply(messages, Response("b", "bob", 1, "Second"), "alice");
        reducer.Apply(messages, Response("a", "alice", 2, "First continued"), "alice");
        Assert.Equal("First continued", messages.Single(m => m.Id == "a").Content);
        Assert.Equal("Second", messages.Single(m => m.Id == "b").Content);
        Assert.Equal("Bob's turn", messages.Single(m => m.Id == "turn").Content);
    }

    [Fact]
    public void RecoveryIgnoresDuplicatesAndOlderRevisionsAndKeepsPartialFailureText()
    {
        var reducer = new RuntimeChatState();
        var messages = new List<ChatMessage>();
        reducer.Apply(messages, Response("a", "alice", 4, "Partial"), "alice");
        Assert.False(reducer.Apply(messages, Response("a", "alice", 3, "Old"), "alice"));
        var failed = Response("a", "alice", 5, "Partial");
        failed.Status = MessageProcessingStatus.Failed;
        failed.Error = "Try again";
        reducer.Apply(messages, failed, "alice");
        Assert.False(reducer.Apply(messages, failed, "alice"));
        Assert.Equal(2, messages.Count);
        Assert.Equal("Partial\n\nTry again", messages[1].Content);
        Assert.False(messages[1].IsStreaming);
        Assert.False(reducer.HasPending("alice"));
    }

    [Fact]
    public async Task HandoffsKeepOneResponseAndEmitOneTerminalCompletion()
    {
        var f = new TestRuntime();
        var accepted = f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "request", false);
        var request = Assert.Single(f.Processor.Requests);
        await request.StreamCallback!(new() { Token = "Hello", AgentType = "GameMaster" });
        await request.StreamCallback!(new() { IsCompleted = true, AgentType = "GameMaster" });
        Assert.False(Assert.Single(f.Runtime.Snapshot("campaign")).IsComplete);
        await request.StreamCallback!(new() { Token = "Fight", AgentType = "Combat" });
        f.Processor.Complete("request");
        await TestRuntime.UntilAsync(() => f.Runtime.Snapshot("campaign").All(m => m.IsComplete));
        await request.StatusCallback!(new() { Status = MessageProcessingStatus.Completed });
        var message = Assert.Single(f.Runtime.Snapshot("campaign"));
        Assert.Equal(accepted.Id, message.Id);
        Assert.Equal("Hello\n\nFight", message.Content);
        var revisions = f.Events.Where(e => e.Method == "MessageUpdated").Select(e => (RuntimeMessage)e.Args[0]!).ToList();
        Assert.Single(revisions.Where(m => m.IsComplete));
        Assert.Equal(revisions.Count, revisions.Select(m => m.Revision).Distinct().Count());
    }

    [Fact]
    public async Task AcceptedRetriesReturnSameWorkAndOtherPendingSubmissionsAreRejected()
    {
        var f = new TestRuntime();
        var first = f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "same", false);
        var retry = f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "same", false);
        Assert.Equal(first.Id, retry.Id);
        Assert.Single(f.Processor.Requests);
        Assert.Throws<HubException>(() => f.Runtime.Submit("campaign", "alice", "a", "Alice", "Again", AgentType.None, "other", false));
        f.Processor.Complete("same");
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        Assert.Equal(first.Id, f.Runtime.Submit("campaign", "alice", "a", "Alice", "Retry", AgentType.None, "same", false).Id);
        Assert.Single(f.Processor.Requests);
    }

    [Fact]
    public async Task FailureAfterTokensRemainsRecoverable()
    {
        var f = new TestRuntime();
        f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "failure", false);
        await Assert.Single(f.Processor.Requests).StreamCallback!(new() { Token = "Partial", AgentType = "GameMaster" });
        f.Processor.Results["failure"].SetException(new Exception("Internal secret"));
        await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        var message = Assert.Single(f.Runtime.Snapshot("campaign"));
        Assert.Equal("Partial", message.Content);
        Assert.Equal(MessageProcessingStatus.Failed, message.Status);
        Assert.DoesNotContain("Internal secret", message.Error!);
    }

    [Fact]
    public async Task RecoveryRetainsOnlyOneHundredCompletedResponsesButNeverDropsActiveWork()
    {
        var f = new TestRuntime();
        for (var i = 0; i < 105; i++)
        {
            var id = i.ToString();
            f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, id, false);
            f.Processor.Complete(id);
            await TestRuntime.UntilAsync(() => !f.Runtime.HasPending("campaign"));
        }
        f.Runtime.Submit("campaign", "alice", "a", "Alice", "Act", AgentType.None, "active", false);
        Assert.Equal(100, f.Runtime.Snapshot("campaign").Count(m => m.IsComplete));
        Assert.Single(f.Runtime.Snapshot("campaign").Where(m => !m.IsComplete));
        f.Processor.Complete("active");
    }

    private static RuntimeMessage Response(string id, string player, long revision, string text) => new()
    { Id = id, ScopeId = "campaign", ClientMessageId = "client-" + id, PlayerId = player, UserText = "Act", Content = text, Revision = revision };
}
