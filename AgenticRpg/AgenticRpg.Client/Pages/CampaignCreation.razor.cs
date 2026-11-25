using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using AgenticRpg.Core.Models.Game;
using ChatMessage = AgenticRpg.Components.ChatComponents.ChatMessage;

namespace AgenticRpg.Client.Pages;

public partial class CampaignCreation : IAsyncDisposable
{
    [SupplyParameterFromQuery(Name = "journey")] public bool StartNewAdventure { get; set; }
    [SupplyParameterFromQuery(Name = "starter")] public string? QuickStarter { get; set; }
    [SupplyParameterFromQuery(Name = "sessionId")] public string? ResumeSessionId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "worldId")]
    public string? WorldId { get; set; }
    [Parameter]
    [SupplyParameterFromQuery(Name = "worldOnly")]
    public bool WorldOnly { get; set; } = true;
    [Inject] private HttpClient HttpClient { get; set; } = default!;
    [Inject] private IWorldService WorldService { get; set; } = default!;
    [Inject] private ILogger<CampaignCreation> Logger { get; set; } = default!;
    [Inject] private NavigationManager NavManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    private string CampaignName { get; set; } = string.Empty;
    private string CampaignDescription { get; set; } = string.Empty;
    private int MaxPlayers { get; set; } = 4;
    private World? CurrentWorld { get; set; }
    private SessionState? CurrentSessionState { get; set; }
    private List<ChatMessage> ChatMessages { get; set; } = [];
    private readonly Dictionary<string, ChatMessage> _streamMessageLookup = new(StringComparer.OrdinalIgnoreCase);
    private bool SubmissionConfirmed;
    private bool IsProcessing { get; set; }
    private readonly RuntimeChatState _runtimeChat = new();
    private string? SaveMessage { get; set; }
    private bool IsSaveError { get; set; }
    private bool _isSaving;
    private bool _completionNavigated;
    private CampaignSettings _createCampaignSettings = new();

    // TODO: Get from authentication/user context
    private string CurrentUserId { get; set; } = "player-1";

    // Session ID for world building
    private string? SessionId { get; set; }

    private MobileView CurrentMobileView { get; set; } = MobileView.Chat;
    private bool ShowModelMenu { get; set; }

    private enum MobileView
    {
        World,
        Chat
    }

    private string SaveButtonLabel => WorldOnly ? (string.IsNullOrEmpty(WorldId) ? "Save World" : "Save Changes") : "Create Campaign";
    private bool CanSaveCampaign => CurrentSessionState?.IsCompleted != true && CurrentWorld is not null && !string.IsNullOrWhiteSpace(CurrentWorld.Name)
        && (WorldOnly ? !string.IsNullOrEmpty(SessionId) : !string.IsNullOrWhiteSpace(CampaignName) && !string.IsNullOrEmpty(WorldId) && !string.IsNullOrEmpty(SessionId));

    protected override void OnParametersSet()
    {
        if (CurrentSessionState is null) return;
        if (ResumeSessionId is not null && ResumeSessionId != CurrentSessionState.SessionId)
        { NavManager.NavigateTo(NavManager.Uri, forceLoad: true); return; }
        WorldOnly = CurrentSessionState.SessionType == SessionType.WorldBuilding;
        WorldId = CurrentSessionState.Context.OriginalWorldId;
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var user = await AuthStateProvider.GetAuthenticationStateAsync();
        if (string.IsNullOrEmpty(user.User.Identity?.Name)) throw new Exception("User not authenticated");
        CurrentUserId = user.User.Identity?.Name ?? "player-1";

        try
        {
            // Check if we're using an existing world
            if (!string.IsNullOrEmpty(WorldId))
            {
                // Load existing world
                CurrentWorld = await WorldService.GetWorldByIdAsync(WorldId);

                if (CurrentWorld == null)
                {
                    SaveMessage = "World not found. Starting fresh world creation.";
                    IsSaveError = true;
                }
                else
                {
                    Logger.LogInformation("Loaded existing world: {WorldId} - {WorldName}", WorldId, CurrentWorld.Name);
                    // Pre-populate campaign name with world name if not set
                    if (string.IsNullOrWhiteSpace(CampaignName))
                    {
                        CampaignName = $"Campaign in {CurrentWorld.Name}";
                    }

                }
            }

            if (string.IsNullOrEmpty(ResumeSessionId))
            {
                // Create world building session via API
                var createSessionRequest = new CreateSessionRequest
                {
                    SessionType = WorldOnly ? SessionType.WorldBuilding : SessionType.CampaignSetup,
                    PlayerId = CurrentUserId, StartNewAdventure = StartNewAdventure, QuickStarter = QuickStarter
                };
                if (!string.IsNullOrEmpty(WorldId))
                {
                    createSessionRequest.WorldId = WorldId;
                }
                var sessionResponse = await HttpClient.PostAsJsonAsync("/api/sessions", createSessionRequest);

                if (!sessionResponse.IsSuccessStatusCode)
                {
                    SaveMessage = "Failed to create session. Please refresh and try again.";
                    IsSaveError = true;
                    return;
                }

                CurrentSessionState = await sessionResponse.Content.ReadFromJsonAsync<SessionState>();
                if (CurrentSessionState == null)
                {
                    SaveMessage = "Failed to initialize session.";
                    IsSaveError = true;
                    return;
                }

                SessionId = CurrentSessionState.SessionId;
                Logger.LogInformation("Created world building session: {SessionId}", SessionId);
            }
            else
            {
                CurrentSessionState = await HttpClient.GetFromJsonAsync<SessionState>($"/api/sessions/{Uri.EscapeDataString(ResumeSessionId)}") ?? throw new InvalidOperationException("Draft not found.");
                SessionId = CurrentSessionState.SessionId;
            }
            WorldOnly = CurrentSessionState!.SessionType == SessionType.WorldBuilding;
            WorldId = CurrentSessionState.Context.OriginalWorldId;
            CurrentWorld = CurrentSessionState.Context.DraftWorld;
            if (CurrentSessionState.Context.DraftCampaign is { } draft)
            { CampaignName = draft.Name; CampaignDescription = draft.Description; _createCampaignSettings = draft.Settings; }
            if (string.IsNullOrEmpty(ResumeSessionId)) NavManager.NavigateTo(NavManager.GetUriWithQueryParameter("sessionId", SessionId), replace: true);
            StartNewAdventure = CurrentSessionState.Context.StartNewAdventure;
            QuickStarter = CurrentSessionState.Context.QuickStarter;
            // Connect to SignalR hub
            await HubService.StartAsync();
            Track(HubService.OnMessageUpdated(HandleRuntimeMessage));
            Track(HubService.OnRecovered(HandleRecovered));

            // Subscribe to session state updates
            Track(HubService.OnSessionStateUpdated(HandleSessionStateUpdated));
            Track(HubService.OnSessionCompleted(HandleSessionCompleted));

            // Join session for real-time updates
            var recovery = await HubService.JoinSessionAsync(SessionId, CurrentUserId, "World Creator", ScopeLeaseId);
            HandleRecovered(recovery);

            if (WorldOnly && !recovery.HasAcceptedRequests && recovery.Messages.Count == 0 && recovery.SessionState?.IsCompleted != true)
                await HubService.SendMessageAsync(SessionId, CurrentUserId, QuickStarter is not null ? StarterCreation.Prompt(false, QuickStarter) : "Introduce yourself, your purpose and begin the world building process", AgentType.WorldBuilder, $"{(QuickStarter is null ? "intro" : "quick")}-{SessionId}");
            SubmissionConfirmed = true;

        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize campaign creation");
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "Failed to connect to World Builder AI. Please refresh the page.",
                IsUser = false,
                Timestamp = DateTime.UtcNow,
                PlayerName = "System"
            });
        }
    }
    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !WorldOnly)
        {
            CurrentMobileView = MobileView.World;
            StateHasChanged();
        }
        return base.OnAfterRenderAsync(firstRender);
    }

    private void HandleSessionStateUpdated(SessionState sessionState)
    {
        if (IsDisposed || sessionState.SessionId != SessionId) return;
        // Update session state and extract draft world
        if (CurrentSessionState is not null && CurrentSessionState.LastUpdatedAt > sessionState.LastUpdatedAt) return;
        CurrentSessionState = sessionState;

        if (sessionState?.Context?.DraftWorld != null)
        {
            CurrentWorld = sessionState.Context.DraftWorld;
            Logger.LogInformation("World draft updated: {Name}", CurrentWorld.Name ?? "(unnamed)");
        }

        InvokeAsync(StateHasChanged);
    }

    private async void HandleSessionCompleted(string sessionId, string resultEntityId)
    {
        if (IsDisposed || sessionId != SessionId || _completionNavigated) return;
        _completionNavigated = true;
        try
        {
            await InvokeAsync(async () =>
            {
                SaveMessage = "Saved successfully!";
                IsSaveError = false;
                StateHasChanged();
                await Task.Delay(500);
                if (IsDisposed) return;
                NavManager.NavigateTo(StartNewAdventure ? $"/adventure-setup?{(WorldOnly ? "worldId" : "campaignId")}={Uri.EscapeDataString(resultEntityId)}" : "/startup");
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Completion navigation failed"); }
    }











    public override async ValueTask DisposeAsync()
    {
        DisposeSubscriptions();
        try { if (!string.IsNullOrEmpty(SessionId)) await HubService.DetachScopeAsync(SessionId, ScopeLeaseId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Scope detach failed"); }
        finally { await base.DisposeAsync(); }
    }

    private async void HandleRuntimeMessage(RuntimeMessage message)
    {
        if (IsDisposed || message.ScopeId != SessionId) return;
        try
        {
            await InvokeAsync(() =>
            {
                if (IsDisposed) return;
                ObserveRuntime(message, CurrentUserId);
                _runtimeChat.Apply(ChatMessages, message, CurrentUserId, null);
                IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(SessionId ?? string.Empty, CurrentUserId);
                StateHasChanged();
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Response refresh failed"); }
    }

    private async void HandleRecovered(ScopeRecoverySnapshot snapshot)
    {
        if (IsDisposed || snapshot.ScopeId != SessionId) return;
        try
        {
            await InvokeAsync(() =>
            {
                if (IsDisposed) return;
                if (snapshot.SessionState is not null) HandleSessionStateUpdated(snapshot.SessionState);
                RememberRecovery(snapshot);
                foreach (var message in snapshot.Messages)
                {
                    ObserveRuntime(message, CurrentUserId);
                    _runtimeChat.Apply(ChatMessages, message, CurrentUserId, null);
                }
                IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(SessionId ?? string.Empty, CurrentUserId);
                if (snapshot.SessionState is { IsCompleted: true, ResultEntityId: not null } session)
                    SaveMessage = $"Already saved: {session.ResultEntityId}";
                StateHasChanged();
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Session recovery failed"); }
    }

    private Task SendMessage(string message) => SendMessageCore(message);
    private Task RetryMessage(ChatMessage message) => SendMessageCore(message.Content, message.IsAuthoritative || message.WasRejected ? null : message.ClientMessageId);
    private Task CancelResponse() => CancelPending(_runtimeChat, SessionId!, CurrentUserId);

    private async Task SendMessageCore(string message, string? retryId = null)
    {
        SubmissionConfirmed = false;
        if (CurrentSessionState?.IsCompleted == true || ReviewRequired || string.IsNullOrWhiteSpace(message) || IsProcessing || _isSaving || !HubConnected)
            return;

        IsProcessing = true;
        var clientMessageId = retryId ?? Guid.NewGuid().ToString();

        // Add user message
        var chatMessage = new ChatMessage
        {
            Id = clientMessageId,
            ClientMessageId = clientMessageId,
            Content = message,
            IsUser = true,
            Timestamp = DateTime.UtcNow,
            PlayerId = CurrentUserId,
            PlayerName = "You", Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Queued
        };
        ChatMessages.RemoveAll(m => m.Id == clientMessageId);
        ChatMessages.Add(chatMessage);

        StateHasChanged();

        try
        {
            // Send message to World Builder Agent via SignalR
            if (string.IsNullOrEmpty(SessionId))
            {
                throw new InvalidOperationException("Session not initialized");
            }

            await HubService.SendMessageAsync(SessionId, CurrentUserId, message, AgentType.WorldBuilder, clientMessageId);
            SubmissionConfirmed = true;

            // The response will come through the HandleReceiveMessage callback
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending message to AI");
            chatMessage.Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Failed;
            chatMessage.WasRejected = ex is Microsoft.AspNetCore.SignalR.HubException;
            chatMessage.StatusNote = chatMessage.WasRejected ? ex.Message : "Delivery not confirmed. Reconnect to check the action.";
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = chatMessage.WasRejected ? ex.Message : "Message delivery was not confirmed. Your text is retained; reconnect and retry safely.",
                IsUser = false,
                Timestamp = DateTime.UtcNow,
                PlayerName = "System"
            });
        }
        finally
        {
            IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(SessionId ?? string.Empty, CurrentUserId);
            StateHasChanged();
        }
    }

    private void HandleMessageDeleted(ChatMessage message)
    {
        if (message != null)
        {
            ChatMessages.Remove(message);
            _streamMessageLookup.Remove(message.Id);
            StateHasChanged();
        }
    }

    private void HandleMessageEdited(ChatMessage message)
    {
        var existingMessage = ChatMessages.FirstOrDefault(m => m.Id == message.Id);
        if (existingMessage != null)
        {
            existingMessage.Content = message.Content;
            StateHasChanged();
        }
    }

    private readonly SemaphoreSlim _draftSaveGate = new(1, 1);
    private async Task SaveDraft()
    {
        await _draftSaveGate.WaitAsync();
        try
        {
        if (WorldOnly || CurrentSessionState is null || CurrentSessionState.IsCompleted || string.IsNullOrEmpty(SessionId)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{SessionId}/campaign-draft");
        request.Headers.TryAddWithoutValidation("If-Match", CurrentSessionState.Revision.ToString());
        request.Content = JsonContent.Create(new Campaign { Name = CampaignName, Description = CampaignDescription,
            WorldId = WorldId!, Settings = _createCampaignSettings, SelectedModel = _createCampaignSettings.DefaultModel });
        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) { IsSaveError = true; SaveMessage = "Changes not saved. Reload the draft or retry saving."; return; }
        CurrentSessionState = await response.Content.ReadFromJsonAsync<SessionState>();
        SaveMessage = null; IsSaveError = false;
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Draft autosave failed"); IsSaveError = true; SaveMessage = "Changes not saved. Your draft remains available; retry saving."; }
        finally { _draftSaveGate.Release(); }
    }
    private async Task SaveCampaign()
    {
        if (ReviewRequired || !CanSaveCampaign || CurrentWorld is null || IsProcessing || _isSaving || !HubConnected) return;
        _isSaving = true;
        SaveMessage = "Saving...";
        IsSaveError = false;
        try
        {
            if (WorldOnly)
            {
                var response = await HttpClient.PostAsync($"/api/sessions/{SessionId}/finalize-world", null);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
                var result = await response.Content.ReadFromJsonAsync<FinalizeWorldResult>();
                HandleSessionCompleted(SessionId!, result?.WorldId ?? CurrentWorld.Id);
            }
            else
            {
                await SaveDraft();
                if (IsSaveError) return;
                var response = await HttpClient.PostAsync($"/api/sessions/{SessionId}/finalize-campaign", null);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
                var result = await response.Content.ReadFromJsonAsync<CampaignResult>();
                HandleSessionCompleted(SessionId!, result?.CampaignId ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            SaveMessage = "Changes not saved. Your draft is available; retry saving.";
            IsSaveError = true;
            Logger.LogError(ex, "Failed to save world or campaign");
        }
        finally { _isSaving = false; }
    }

    // Result type for API response
    private sealed class CampaignResult { public string CampaignId { get; set; } = string.Empty; }
    private class FinalizeWorldResult
    {
        public string WorldId { get; set; } = string.Empty;
    }

    private void OpenCompletedResult()
    {
        NavManager.NavigateTo(StartNewAdventure ? $"/adventure-setup?{(WorldOnly ? "worldId" : "campaignId")}={Uri.EscapeDataString(CurrentSessionState?.ResultEntityId ?? "")}" : "/startup");
    }

    private void GoBack()
    {
        NavManager.NavigateTo("/startup");
    }

    private void SetMobileView(MobileView view)
    {
        CurrentMobileView = view;
        StateHasChanged();
    }

    private void ToggleModelMenu()
    {
        ShowModelMenu = !ShowModelMenu;
    }

    private void CloseModelMenu()
    {
        ShowModelMenu = false;
    }
}
