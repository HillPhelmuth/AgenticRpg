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
    private bool IsProcessing { get; set; }
    private string? SaveMessage { get; set; }
    private bool IsSaveError { get; set; }
    private bool IsConnected { get; set; }

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

    private bool CanSaveCharacter => 
        CurrentCharacter != null && 
        !string.IsNullOrWhiteSpace(CurrentCharacter.Name) &&
        CurrentCharacter.Race != CharacterRace.None &&
        CurrentCharacter.Class != CharacterClass.None;

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

            // Create session state via API
            var sessionResponse = await HttpClient.PostAsJsonAsync("/api/sessions", new
            {
                SessionType = 0, // CharacterCreation = 0
                PlayerId = CurrentUserId
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

            // Connect to SignalR hub
            await HubService.StartAsync();
            
            // Subscribe to session state updates
            HubService.OnSessionStateUpdated(HandleSessionStateUpdated);
            HubService.OnReceiveMessage(HandleReceiveMessage);
            HubService.OnSessionCompleted(HandleSessionCompleted);
            
            // Join session for real-time updates
            await HubService.JoinSessionAsync(SessionId, CurrentUserId, "Character Creator");
            IsConnected = true;

            // Initialize with welcome message from Character Creation AI
            
            await HubService.SendMessageAsync(SessionId, CurrentUserId, "Introduce yourself, your purpose and begin the character creation process", AgentType.CharacterCreation);
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
        // Update session state and extract draft character
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
    
    private void HandleSessionCompleted(string sessionId, string resultEntityId)
    {
        Logger.LogInformation("Session {SessionId} completed with character {CharacterId}", 
            sessionId, resultEntityId);
        
        // Session completed - navigate to appropriate page
        InvokeAsync(async () =>
        {
            await Task.Delay(500);
            if (!string.IsNullOrEmpty(CampaignId))
            {
                NavManager.NavigateTo($"/lobby/{CampaignId}");
            }
            else
            {
                NavManager.NavigateTo("/startup");
            }
        });
    }

    private void HandleReceiveMessage(string playerId, AgentResponse message, DateTime timestamp)
    {
        // Receive AI responses from Character Creation Agent
        if (playerId != CurrentUserId)
        {
            ChatMessages.Add(new ChatMessage
            {
                Id =message.Id,
                Content = message.FormattedResponse?.MessageToPlayers ?? message.Message,
                IsUser = false,
                Timestamp = timestamp,
                PlayerId = playerId,
                PlayerName = "Character Creation AI",
                SuggestedActions = message.FormattedResponse?.SuggestedActions?.FirstOrDefault()?.Suggestions ?? []
            });
            InvokeAsync(StateHasChanged);
        }
    }

    private async Task SendMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        IsProcessing = true;

        // Add user message
        ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Content = message,
            IsUser = true,
            Timestamp = DateTime.UtcNow,
            PlayerId = CurrentUserId,
            PlayerName = "You"
        });

        StateHasChanged();

        try
        {
            // Send message to Character Creation Agent via SignalR
            if (string.IsNullOrEmpty(SessionId))
            {
                throw new InvalidOperationException("Session not initialized");
            }

            await HubService.SendMessageAsync(SessionId, CurrentUserId, message, AgentType.CharacterCreation);

            // The response will come through the HandleReceiveMessage callback
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending message to AI");
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Error communicating with AI: {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.UtcNow,
                PlayerName = "System"
            });
        }
        finally
        {
            IsProcessing = false;
            StateHasChanged();
        }
    }

    private void HandleMessageDeleted(ChatMessage message)
    {
        if (message != null)
        {
            ChatMessages.Remove(message);
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
        if (!CanSaveCharacter || CurrentCharacter == null || string.IsNullOrEmpty(SessionId))
            return;

        try
        {
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
            
            // Navigate based on whether we're creating for a campaign or standalone
            await Task.Delay(1500);
            if (!string.IsNullOrEmpty(CampaignId))
            {
                NavManager.NavigateTo($"/lobby/{CampaignId}");
            }
            else
            {
                NavManager.NavigateTo("/startup");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save character");
            SaveMessage = $"Failed to save: {ex.Message}";
            IsSaveError = true;
        }
    }
    
    // Result type for API response
    private class FinalizeCharacterResult
    {
        public string CharacterId { get; set; } = string.Empty;
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

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsConnected && !string.IsNullOrEmpty(SessionId))
            {
                await HubService.LeaveSessionAsync(SessionId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during disposal");
        }
    }
}
