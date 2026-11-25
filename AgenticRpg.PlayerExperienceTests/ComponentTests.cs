using System.Net;
using System.Net.Http.Json;
using AgenticRpg.Client.Pages;
using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Components.GameComponents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class ComponentTests
{
    [Fact]
    public async Task CreationRequirementsAndJourneyRemainAvailableBehindCompactHeaderControl()
    {
        await using var context = new BunitContext();
        var progress = context.Render<CreationProgress>(p => p.Add(c => c.Step, 3)
            .AddChildContent("<p>Still required: character draft</p>"));
        Assert.Contains("Character (3/4)", progress.Find("summary").TextContent);
        Assert.False(progress.Find("details").HasAttribute("open"));
        Assert.Equal("step", progress.Find(".setup-progress .current").GetAttribute("aria-current"));
        Assert.Contains("Still required", progress.Find(".creation-status").TextContent);
    }

    [Fact]
    public async Task LongSuggestionsRemainSelectableBehindOneCompactControl()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var suggestions = Enumerable.Range(1, 4).Select(i => $"Path {i}: a very long suggestion with all its original details").ToList();
        string? selected = null;
        var chat = context.Render<ChatView>(p => p.Add(c => c.Messages,
            [new() { Id = "response", Content = "Pick your disaster", SuggestedActions = suggestions }])
            .Add(c => c.OnSuggestedActionInvoked, action => selected = action));
        Assert.Contains("(4)", chat.Find(".suggested-actions summary").TextContent);
        Assert.False(chat.Find(".suggested-actions").HasAttribute("open"));
        chat.FindAll(".suggested-action-btn")[2].Click();
        Assert.Equal(suggestions[2], selected);
    }

    [Fact]
    public async Task EarlierBubbleRendersItsTerminalStatusAndFailureAfterAnotherBubbleAppears()
    {
        await using var context = new BunitContext();
        var user = new ChatMessage { Id = "user", IsUser = true, Content = "Act",
            Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Queued };
        var userView = context.Render<MessageView>(p => p.Add(c => c.Message, user).Add(c => c.IsLastMessage, false));
        user.Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Completed;
        userView.Render(p => p.Add(c => c.Message, user));
        Assert.Contains("Resolved", userView.Markup);
        var ai = new ChatMessage { Id = "response", Content = "Partial", IsStreaming = true };
        var responseView = context.Render<MessageView>(p => p.Add(c => c.Message, ai).Add(c => c.IsLastMessage, false));
        ai.IsStreaming = false;
        ai.Content = "Partial. Please try again.";
        responseView.Render(p => p.Add(c => c.Message, ai));
        Assert.Contains("Please try again", responseView.Markup);
    }

    [Fact]
    public async Task PendingAndDisconnectedInputsCannotSubmitIncludingSuggestedActionPath()
    {
        await using var context = new BunitContext();
        var sent = new List<string>();
        var input = context.Render<UserInput>(p => p.Add(c => c.IsProcessing, true).Add(c => c.OnSubmit, sent.Add));
        Assert.True(input.Find(".btn-submit").HasAttribute("disabled"));
        Assert.DoesNotContain("Cancel", input.Markup);
        await input.InvokeAsync(() => input.Instance.SubmitMessage("Suggested action"));
        Assert.Empty(sent);
        input.Render(p => p.Add(c => c.IsProcessing, false).Add(c => c.IsUnavailable, true));
        await input.InvokeAsync(() => input.Instance.SubmitMessage("Another action"));
        Assert.Empty(sent);
        input.Render(p => p.Add(c => c.IsUnavailable, false));
        await input.InvokeAsync(() => input.Instance.SubmitMessage("Valid action"));
        Assert.Single(sent);
    }

    [Fact]
    public async Task SuggestedControlsDisableDuringWorkAndChatDisposesScrollTracking()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./_content/AgenticRpg.Components/ChatComponents/ChatView.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        var calls = 0;
        var chat = context.Render<ChatView>(p => p.Add(c => c.IsProcessing, true)
            .Add(c => c.Messages, [new() { Id = "response", Content = "Ready", SuggestedActions = ["Open door"] }])
            .Add(c => c.OnSuggestedActionInvoked, _ => calls++));
        Assert.True(chat.Find(".suggested-action-btn").HasAttribute("disabled"));
        chat.Find(".suggested-action-btn").Click();
        Assert.Equal(0, calls);
        await chat.Instance.DisposeAsync();
        Assert.Single(module.Invocations["disposeScrollTracking"]);
    }

    [Theory]
    [InlineData(true, null, "Save World")]
    [InlineData(true, "world", "Save Changes")]
    [InlineData(false, "world", "Create Campaign")]
    public async Task WorldAndCampaignSaveControlsMatchModeAndWorldNeedsNoCampaignName(bool worldOnly, string? worldId, string label)
    {
        await using var page = new PageFixture(SessionType.WorldBuilding);
        page.Session.SessionType = worldOnly ? SessionType.WorldBuilding : SessionType.CampaignSetup;
        page.Session.Context.OriginalWorldId = worldId;
        page.Session.Context.DraftWorld = new() { Id = "world", Name = "Valid world" };
        page.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/campaign-creation?worldOnly={worldOnly}&worldId={worldId}");
        var cut = page.Context.Render<CampaignCreation>();
        cut.WaitForAssertion(() => Assert.Contains(label, cut.Markup));
        Assert.False(cut.Find(".btn-save").HasAttribute("disabled"));
        Assert.True(page.JoinSawSubscriptions);
        await page.Context.DisposeAsync();
        Assert.All(page.Handles, h => Assert.True(h.Disposed));
        page.Hub.Verify(h => h.DetachScopeAsync(page.Session.SessionId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FinishCharacterExplainsMissingFieldsAndDisposesSubscriptionsAndBaseEvents()
    {
        await using var page = new PageFixture(SessionType.CharacterCreation);
        page.Session.Context.DraftCharacter = new() { Attributes = [], Level = 0 };
        var cut = page.Context.Render<CharacterCreation>();
        cut.WaitForAssertion(() => Assert.Contains("Finish Character", cut.Markup));
        var save = cut.FindAll("button").Single(b => b.TextContent.Contains("Finish Character"));
        Assert.True(save.HasAttribute("disabled"));
        foreach (var missing in new[] { "name", "race", "class", "attributes", "hit points" }) Assert.Contains(missing, cut.Markup);
        var currentUri = page.Context.Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains("sessionId=", currentUri);
        page.Completed!("another-session", "unrelated");
        Assert.Equal(currentUri, page.Context.Services.GetRequiredService<NavigationManager>().Uri);
        await page.Context.DisposeAsync();
        Assert.All(page.Handles, h => Assert.True(h.Disposed));
        page.Hub.VerifyRemove(h => h.OnDiceRollResult -= It.IsAny<EventHandler<List<AgenticRpg.DiceRoller.Models.RollDiceResults>>>(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task NarrativeControlHasNoGmTabAndDeduplicatesPublicEntries()
    {
        await using var context = new BunitContext();
        var narratives = TestRuntime.Narratives();
        narratives.Add(narratives[0]);
        var cut = context.Render<NarrativeView>(p => p.Add(c => c.Narratives, narratives).Add(c => c.CurrentCharacterId, "a"));
        Assert.DoesNotContain("GM Only", cut.Markup);
        Assert.Single(cut.FindAll(".narrative-entry"));
        await cut.InvokeAsync(() => cut.Instance.SwitchTab(NarrativeView.NarrativeTab.Character));
        Assert.Contains("Alice", cut.Markup);
        Assert.DoesNotContain("Bob", cut.Markup);
    }

    [Fact]
    public async Task CampaignCharacterCompletionUsesLobbyQueryRouteAndNavigatesOnce()
    {
        await using var page = new PageFixture(SessionType.CharacterCreation);
        page.Session.Context.DraftCharacter = TestRuntime.Character();
        page.Session.Context.CampaignId = "campaign";
        var cut = page.Context.Render<CharacterCreation>(p => p.Add(c => c.CampaignId, "campaign"));
        cut.WaitForAssertion(() => Assert.NotNull(page.Completed));
        var navigation = page.Context.Services.GetRequiredService<NavigationManager>();
        var navigated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        navigation.LocationChanged += (_, e) => navigated.TrySetResult(e.Location);
        page.Completed!(page.Session.SessionId, "completed-character");
        Assert.EndsWith("/lobby?campaignId=campaign&characterId=completed-character",
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        page.Completed!(page.Session.SessionId, "duplicate-event");
        Assert.EndsWith("characterId=completed-character", page.Context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task ResumeRestoresWorldModeAndOriginalIdWhenQueryParametersAreMissing()
    {
        await using var page = new PageFixture(SessionType.WorldBuilding);
        page.Session.Context.OriginalWorldId = "world";
        page.Session.Context.DraftWorld = new() { Id = "world", Name = "Restored world" };
        page.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/campaign-creation?sessionId={page.Session.SessionId}");
        var cut = page.Context.Render<CampaignCreation>();
        cut.WaitForAssertion(() => Assert.Contains("Save Changes", cut.Markup));
        Assert.Contains("Edit World", cut.Markup);
        Assert.DoesNotContain("Campaign Name", cut.Markup);
    }

    [Fact]
    public async Task CompletedDraftShowsResultAndDisablesGenerationAndFinalization()
    {
        await using var page = new PageFixture(SessionType.CharacterCreation);
        page.Session.Context.DraftCharacter = TestRuntime.Character();
        page.Session.IsCompleted = true; page.Session.ResultEntityId = "completed-result";
        var navigation = page.Context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/character-creation?sessionId={page.Session.SessionId}");
        var cut = page.Context.Render<CharacterCreation>();
        cut.WaitForAssertion(() => Assert.Contains("completed-result", cut.Markup));
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Contains("Finish Character")).HasAttribute("disabled"));
        Assert.True(cut.Find(".input-field").HasAttribute("disabled"));
        Assert.Contains("sessionId=", navigation.Uri);
        page.Hub.Verify(h => h.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentType>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(SessionType.CharacterCreation, "Warrior")]
    [InlineData(SessionType.WorldBuilding, "Classic fantasy")]
    public async Task QuickCreateSendsOneDeduplicatedRequestAndSuppressesIntroduction(SessionType type, string starter)
    {
        await using var page = new PageFixture(type);
        page.NoHistory = true;
        page.Session.Context.QuickStarter = starter;
        page.Session.Context.StartNewAdventure = true;
        var navigation = page.Context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(type == SessionType.CharacterCreation ? "/character-creation" : "/campaign-creation?worldOnly=true");
        if (type == SessionType.CharacterCreation) page.Context.Render<CharacterCreation>();
        else page.Context.Render<CampaignCreation>();
        page.Hub.Verify(h => h.SendMessageAsync(page.Session.SessionId, "alice", It.Is<string>(s => s.Contains(starter) && s.Contains("Do not finalize or save")), It.IsAny<AgentType>(), $"quick-{page.Session.SessionId}"), Times.Once);
        page.Hub.Verify(h => h.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentType>(), It.Is<string>(s => s.StartsWith("intro-"))), Times.Never);
    }

    [Fact]
    public async Task RecoveryGuardsBlockSubmissionButLeaveDraftingAvailable()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var input = context.Render<UserInput>(p => p.Add(c => c.IsUnavailable, true));
        Assert.False(input.Find(".input-field").HasAttribute("disabled"));
        Assert.True(input.Find(".btn-submit").HasAttribute("disabled"));
    }

    private sealed class Handle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public async Task GameplayDefaultsToChatAndCountsUnreadResponsesUntilChatOpens()
    {
        await using var page = new PageFixture(SessionType.CharacterCreation);
        var actor = TestRuntime.Character();
        var characters = new Mock<ICharacterService>();
        characters.Setup(c => c.GetCharactersByCampaignAsync("campaign")).ReturnsAsync([actor]);
        page.Context.Services.AddSingleton(characters.Object);
        page.Context.Services.AddSingleton(Mock.Of<IGameStateService>());
        var state = new GameState { Campaign = new() { Id = "campaign" }, Characters = [actor] };
        page.Hub.Setup(h => h.JoinCampaignAsync("campaign", "alice", "a", actor.Name, It.IsAny<string>()))
            .ReturnsAsync(new ScopeRecoverySnapshot { ScopeId = "campaign", GameState = state, HasAcceptedRequests = true });
        Action<RuntimeMessage>? updated = null;
        page.Hub.Setup(h => h.OnMessageUpdated(It.IsAny<Action<RuntimeMessage>>()))
            .Callback((Action<RuntimeMessage> callback) => updated = callback).Returns(new Handle());
        page.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("/game?campaignId=campaign&characterId=a");
        var cut = page.Context.Render<AgenticRpg.Client.Pages.Game>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".chat-panel.mobile-visible")));
        Assert.Single(cut.FindAll(".adventure-overview"));
        cut.FindAll(".mobile-view-toggle button")[0].Click();
        updated!(new() { ScopeId = "campaign", Id = "response", PlayerId = "alice", ClientMessageId = "action",
            Content = "New narration", Revision = 1, Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Completed });
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".unread-badge").TextContent));
        cut.FindAll(".mobile-view-toggle button")[1].Click();
        Assert.Empty(cut.FindAll(".unread-badge"));
        Assert.Single(cut.FindAll(".chat-panel.mobile-visible"));
    }

    private sealed class PageFixture : IAsyncDisposable
    {
        public BunitContext Context { get; } = new();
        public Mock<IGameHubService> Hub { get; } = new();
        public SessionState Session { get; }
        public List<Handle> Handles { get; } = [];
        public bool JoinSawSubscriptions { get; private set; }
        public bool NoHistory { get; set; }
        public Action<string, string>? Completed { get; private set; }
        private IDisposable Subscribe() { var handle = new Handle(); Handles.Add(handle); return handle; }
        public PageFixture(SessionType type)
        {
            Session = new() { SessionType = type, PlayerId = "alice" };
            Context.AddAuthorization().SetAuthorized("alice");
            Context.JSInterop.Mode = JSRuntimeMode.Loose;
            Context.ComponentFactories.AddStub(t => t.Name is "WorldDetails" or "CharacterSheetView" or "RpgModelSelect" or "ChatView");
            Context.Services.AddLogging();
            Context.Services.AddSingleton(Hub.Object);
            var campaigns = new Mock<ICampaignService>();
            campaigns.Setup(c => c.GetCampaignByIdAsync(It.IsAny<string>())).ReturnsAsync(new Campaign { Id = "campaign", Name = "Campaign" });
            Context.Services.AddSingleton(campaigns.Object);
            Context.Services.AddSingleton(Mock.Of<ICharacterService>());
            var worlds = new Mock<IWorldService>();
            worlds.Setup(w => w.GetWorldByIdAsync(It.IsAny<string>())).ReturnsAsync(() => Session.Context.DraftWorld);
            Context.Services.AddSingleton(worlds.Object);
            Context.Services.AddSingleton(new AudioInterop(Context.JSInterop.JSRuntime));
            Context.Services.AddSingleton(new HttpClient(new Handler(Session)) { BaseAddress = new("http://localhost") });
            Hub.SetupGet(h => h.ConnectionStatus).Returns(GameConnectionStatus.Connected);
            Hub.Setup(h => h.StartAsync()).Returns(Task.CompletedTask);
            Hub.Setup(h => h.OnConnectionStatusChanged(It.IsAny<Action<GameConnectionStatus>>())).Returns(Subscribe);
            Hub.Setup(h => h.OnRecovered(It.IsAny<Action<ScopeRecoverySnapshot>>())).Returns(Subscribe);
            Hub.Setup(h => h.OnMessageUpdated(It.IsAny<Action<RuntimeMessage>>())).Returns(Subscribe);
            Hub.Setup(h => h.OnSessionStateUpdated(It.IsAny<Action<SessionState>>())).Returns(Subscribe);
            Hub.Setup(h => h.OnSessionCompleted(It.IsAny<Action<string, string>>()))
                .Callback((Action<string, string> callback) => Completed = callback).Returns(Subscribe);
            Hub.Setup(h => h.JoinSessionAsync(It.IsAny<string>(), "alice", It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    JoinSawSubscriptions = Handles.Count >= 5;
                    return new ScopeRecoverySnapshot { ScopeId = Session.SessionId, SessionState = Session,
                        Messages = NoHistory ? [] : [new() { Id = "old", ClientMessageId = "old-client", ScopeId = Session.SessionId,
                            PlayerId = "alice", Revision = 1, Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Completed }] };
                });
            Hub.Setup(h => h.DetachScopeAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
    private sealed class Handler(SessionState session) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(session) });
    }
}
