using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using System.Text.Json;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Client.Pages;

public partial class CharacterCreation : IAsyncDisposable
{
    [SupplyParameterFromQuery(Name = "journey")] public bool StartNewAdventure { get; set; }
    [SupplyParameterFromQuery(Name = "starter")] public string? QuickStarter { get; set; }
    [SupplyParameterFromQuery(Name = "sessionId")] public string? ResumeSessionId { get; set; }

    [Parameter] public string? CampaignId { get; set; }

    [Inject] private ICharacterService CharacterService { get; set; } = default!;
    [Inject] private HttpClient HttpClient { get; set; } = default!;
    [Inject] private ILogger<CharacterCreation> Logger { get; set; } = default!;
    [Inject] private NavigationManager NavManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    private Character? CurrentCharacter { get; set; }
    private Campaign? Campaign { get; set; }
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

    // TODO: Get from authentication/user context
    private string CurrentUserId { get; set; } = "player-1";

    // Session ID for character creation (created via API)
    private string? SessionId { get; set; }

    private MobileView CurrentMobileView { get; set; } = MobileView.Chat;
    private bool ShowModelMenu { get; set; }

    private enum MobileView
    {
        Character,
        Chat
    }

    private List<string> MissingCharacterRequirements => AgenticRpg.Core.Services.SessionCompletionService.CharacterMissingRequirements(CurrentCharacter);
    private bool CanSaveCharacter => CurrentSessionState?.IsCompleted != true && MissingCharacterRequirements.Count == 0 && !string.IsNullOrEmpty(SessionId);

    protected override void OnParametersSet()
    {
        if (CurrentSessionState is null) return;
        if (ResumeSessionId is not null && ResumeSessionId != CurrentSessionState.SessionId)
        { NavManager.NavigateTo(NavManager.Uri, forceLoad: true); return; }
        CampaignId = CurrentSessionState.Context.CampaignId;
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        //HubService.OnDiceRollResult += HandleDiceRollResult;
        try
        {
            var user = await AuthStateProvider.GetAuthenticationStateAsync();
            if (string.IsNullOrEmpty(user.User.Identity?.Name)) throw new Exception("User not authenticated");
            CurrentUserId = user.User.Identity?.Name ?? "player-1";

            // Load campaign details if campaignId provided
            if (!string.IsNullOrEmpty(CampaignId))
            {
                Campaign = await CampaignService.GetCampaignByIdAsync(CampaignId);
                if (Campaign == null)
                {
                    SaveMessage = "Campaign not found.";
                    IsSaveError = true;
                    return;
                }
            }

            if (string.IsNullOrEmpty(ResumeSessionId))
            {
                // Create session state via API
                var sessionResponse = await HttpClient.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
                {
                    SessionType = SessionType.CharacterCreation, PlayerId = CurrentUserId, CampaignId = CampaignId,
                    StartNewAdventure = StartNewAdventure, QuickStarter = QuickStarter
                });

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
                Logger.LogInformation("Created character creation session: {SessionId}", SessionId);
            }
            else
            {
                CurrentSessionState = await HttpClient.GetFromJsonAsync<SessionState>($"/api/sessions/{Uri.EscapeDataString(ResumeSessionId)}") ?? throw new InvalidOperationException("Draft not found.");
                SessionId = CurrentSessionState.SessionId;
            }
            if (CurrentSessionState!.SessionType != SessionType.CharacterCreation) throw new InvalidOperationException("Incorrect draft type.");
            CampaignId = CurrentSessionState.Context.CampaignId;
            CurrentCharacter = CurrentSessionState.Context.DraftCharacter;
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
            var recovery = await HubService.JoinSessionAsync(SessionId, CurrentUserId, "Character Creator", ScopeLeaseId);
            HandleRecovered(recovery);


            // Initialize with welcome message from Character Creation AI

            if (!recovery.HasAcceptedRequests && recovery.Messages.Count == 0 && recovery.SessionState?.IsCompleted != true)
                await HubService.SendMessageAsync(SessionId, CurrentUserId, QuickStarter is not null ? StarterCreation.Prompt(true, QuickStarter) : "Introduce yourself, your purpose and begin the character creation process", AgentType.CharacterCreation, $"{(QuickStarter is null ? "intro" : "quick")}-{SessionId}");
            SubmissionConfirmed = true;
            //ChatMessages.Add(new ChatMessage
            //{
            //    Id = Guid.NewGuid().ToString(),
            //    Content = welcomeMessage,
            //    IsUser = false,
            //    Timestamp = DateTime.UtcNow,
            //    PlayerName = "Character Creation AI"
            //});
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize character creation");
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "Failed to connect to Character Creation AI. Please refresh the page.",
                IsUser = false,
                Timestamp = DateTime.UtcNow,
                PlayerName = "System"
            });
        }
    }


    private void HandleSessionStateUpdated(SessionState sessionState)
    {
        if (IsDisposed || sessionState.SessionId != SessionId) return;
        // Update session state and extract draft character
        if (CurrentSessionState is not null && CurrentSessionState.LastUpdatedAt > sessionState.LastUpdatedAt) return;
        CurrentSessionState = sessionState;

        if (sessionState?.Context?.DraftCharacter != null)
        {
            CurrentCharacter = sessionState.Context.DraftCharacter;
            Logger.LogInformation("Character draft updated: {Name}, {Race}, {Class}",
                CurrentCharacter.Name ?? "(unnamed)",
                CurrentCharacter.Race,
                CurrentCharacter.Class);
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
                if (!string.IsNullOrEmpty(CampaignId))
                NavManager.NavigateTo($"/lobby?campaignId={CampaignId}&characterId={resultEntityId}{(StartNewAdventure ? "&journey=true" : "")}");
            else NavManager.NavigateTo("/startup");
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Completion navigation failed"); }
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
            // Send message to Character Creation Agent via SignalR
            if (string.IsNullOrEmpty(SessionId))
            {
                throw new InvalidOperationException("Session not initialized");
            }

            await HubService.SendMessageAsync(SessionId, CurrentUserId, message, AgentType.CharacterCreation, clientMessageId);
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

    private async Task SaveCharacter()
    {
        if (ReviewRequired || !CanSaveCharacter || CurrentCharacter == null || string.IsNullOrEmpty(SessionId) || IsProcessing || _isSaving || !HubConnected)
            return;

        try
        {
            _isSaving = true;
            SaveMessage = "Saving character...";
            IsSaveError = false;
            StateHasChanged();

            // Finalize the session - this will save the character and complete the session
            var response = await HttpClient.PostAsync($"/api/sessions/{SessionId}/finalize-character", null);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to finalize character: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<FinalizeCharacterResult>();
            var characterId = result?.CharacterId ?? CurrentCharacter.Id;

            SaveMessage = "Character saved successfully!";
            Logger.LogInformation("Character {CharacterId} finalized from session {SessionId}",
                characterId, SessionId);

            HandleSessionCompleted(SessionId, characterId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save character");
            SaveMessage = "Changes not saved. Your draft is available; retry saving.";
            IsSaveError = true;
        }
        finally { _isSaving = false; }
    }

    // Result type for API response
    private class FinalizeCharacterResult
    {
        public string CharacterId { get; set; } = string.Empty;
    }

    private void OpenCompletedResult()
    {
        if (CurrentSessionState?.ResultEntityId is { } id && !string.IsNullOrEmpty(CurrentSessionState.Context.CampaignId))
            NavManager.NavigateTo($"/lobby?campaignId={Uri.EscapeDataString(CurrentSessionState.Context.CampaignId)}&characterId={Uri.EscapeDataString(id)}{(StartNewAdventure ? "&journey=true" : "")}");
        else NavManager.NavigateTo("/startup");
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

    public override async ValueTask DisposeAsync()
    {
        DisposeSubscriptions();
        try { if (!string.IsNullOrEmpty(SessionId)) await HubService.DetachScopeAsync(SessionId, ScopeLeaseId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Scope detach failed"); }
        finally { await base.DisposeAsync(); }
    }
}
