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
    private bool IsProcessing { get; set; }
    private string? SaveMessage { get; set; }
    private bool IsSaveError { get; set; }
    private bool IsConnected { get; set; }

    // TODO: Get from authentication/user context
    private string CurrentUserId { get; set; } = "player-1";

    // Session ID for world building
    private string? SessionId { get; set; }

    private MobileView CurrentMobileView { get; set; } = MobileView.World;

    private enum MobileView
    {
        World,
        Chat
    }

    private bool CanSaveCampaign => 
        !string.IsNullOrWhiteSpace(CampaignName) && 
        CurrentWorld != null && 
        !string.IsNullOrWhiteSpace(CurrentWorld.Name) &&
        (!string.IsNullOrEmpty(WorldId) || !string.IsNullOrEmpty(SessionId));

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
                    // Skip world building session creation
                    return;
                }
            }
            
            // Create world building session via API
            var sessionResponse = await HttpClient.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
            {
                SessionType = SessionType.WorldBuilding, // WorldBuilding = 1
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
            Logger.LogInformation("Created world building session: {SessionId}", SessionId);
            
            // Connect to SignalR hub
            await HubService.StartAsync();
            
            // Subscribe to session state updates
            HubService.OnSessionStateUpdated(HandleSessionStateUpdated);
            HubService.OnReceiveMessage(HandleReceiveMessage);
            HubService.OnSessionCompleted(HandleSessionCompleted);
            
            // Join session for real-time updates
            await HubService.JoinSessionAsync(SessionId, CurrentUserId, "World Creator");
            IsConnected = true;
            await HubService.SendMessageAsync(SessionId, CurrentUserId, "Introduce yourself, your purpose and begin the world building process", AgentType.WorldBuilder);
            
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

    private void HandleSessionStateUpdated(SessionState sessionState)
    {
        // Update session state and extract draft world
        CurrentSessionState = sessionState;
        
        if (sessionState?.Context?.DraftWorld != null)
        {
            CurrentWorld = sessionState.Context.DraftWorld;
            Logger.LogInformation("World draft updated: {Name}", CurrentWorld.Name ?? "(unnamed)");
        }
        
        InvokeAsync(StateHasChanged);
    }
    
    private void HandleSessionCompleted(string sessionId, string resultEntityId)
    {
        Logger.LogInformation("Session {SessionId} completed with world {WorldId}", 
            sessionId, resultEntityId);
        
        // Session completed - navigate back
        InvokeAsync(async () =>
        {
            await Task.Delay(500);
            NavManager.NavigateTo("/startup");
        });
    }

    private void HandleReceiveMessage(string playerId, AgentResponse message, DateTime timestamp)
    {
        // Receive AI responses from World Builder Agent
        if (playerId != CurrentUserId)
        {
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = message.FormattedResponse?.MessageToPlayers ?? message.Message,
                IsUser = false,
                Timestamp = timestamp,
                PlayerId = playerId,
                PlayerName = "World Builder AI",
                SuggestedActions = message.FormattedResponse?.SuggestedActions?.FirstOrDefault()?.Suggestions ?? []
            });
            InvokeAsync(StateHasChanged);
        }
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
            // Send message to World Builder Agent via SignalR
            if (string.IsNullOrEmpty(SessionId))
            {
                throw new InvalidOperationException("Session not initialized");
            }
            
            await HubService.SendMessageAsync(SessionId, CurrentUserId, message,AgentType.WorldBuilder);
            
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

    private async Task SaveCampaign()
    {
        if (!CanSaveCampaign || CurrentWorld == null)
            return;

        try
        {
            SaveMessage = "Saving campaign...";
            IsSaveError = false;
            StateHasChanged();

            string worldId;
            
            // Check if we're using an existing world or need to finalize from session
            if (!string.IsNullOrEmpty(WorldId))
            {
                // Using existing world
                worldId = WorldId;
            }
            else if (!string.IsNullOrEmpty(SessionId))
            {
                // Finalize the world from the session
                var worldResponse = await HttpClient.PostAsync($"/api/sessions/{SessionId}/finalize-world", null);
                
                if (!worldResponse.IsSuccessStatusCode)
                {
                    var errorContent = await worldResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to finalize world: {errorContent}");
                }
                
                var worldResult = await worldResponse.Content.ReadFromJsonAsync<FinalizeWorldResult>();
                worldId = worldResult?.WorldId ?? CurrentWorld.Id;
            }
            else
            {
                throw new InvalidOperationException("No world or session available to create campaign");
            }

            // Create campaign with the finalized world
            var campaign = new Campaign
            {
                Id = Guid.NewGuid().ToString(),
                Name = CampaignName,
                Description = CampaignDescription,
                OwnerId = CurrentUserId,
                WorldId = worldId,
                Status = CampaignStatus.Setup
            };

            await CampaignService.CreateCampaignAsync(campaign);
            
            SaveMessage = "Campaign saved successfully!";
            Logger.LogInformation("Campaign {CampaignId} created with world {WorldId}", campaign.Id, worldId);
            
            // Navigate back to startup menu after short delay
            await Task.Delay(1500);
            NavManager.NavigateTo("/startup");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save campaign");
            SaveMessage = $"Failed to save: {ex.Message}";
            IsSaveError = true;
        }
    }
    
    // Result type for API response
    private class FinalizeWorldResult
    {
        public string WorldId { get; set; } = string.Empty;
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
}
