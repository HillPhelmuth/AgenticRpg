using System.Text.Json;
using System.Threading;
using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgenticRpg.Client.Pages;

public partial class Game : IAsyncDisposable
{
    [Parameter]
    [SupplyParameterFromQuery(Name = "campaignId")]
    public string CampaignId { get; set; } = string.Empty;

    [Parameter]
    [SupplyParameterFromQuery(Name = "characterId")]
    public string? CharacterId { get; set; }

    [Inject] private ICharacterService CharacterService { get; set; } = default!;
    [Inject] private IGameStateService GameStateService { get; set; } = default!;
    [Inject] private ILogger<Game> Logger { get; set; } = default!;
    [Inject] private NavigationManager NavManager { get; set; } = default!;


    private Campaign? Campaign { get; set; }
    private Character? CurrentCharacter { get; set; }
    private GameState? GameState { get; set; }
    private CombatEncounter? CurrentCombatEncounter { get; set; }
    private List<ChatMessage> ChatMessages { get; set; } = [];
    private readonly Dictionary<string, ChatMessage> _messageLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatMessage> _streamMessageLookup = new(StringComparer.OrdinalIgnoreCase);
    private bool SubmissionConfirmed;
    private bool IsProcessing { get; set; }
    private readonly RuntimeChatState _runtimeChat = new();
    private string? ErrorMessage { get; set; }
    private string ActiveTab { get; set; } = "story";
    private bool ShowPartyView { get; set; }
    private new UserInput? UserInputRef { get; set; }
    private MobileView CurrentMobileView { get; set; } = MobileView.Chat;
    private readonly HashSet<string> _unreadResponses = [];
    private bool ShowModelMenu { get; set; }
    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    private enum MobileView
    {
        Game,
        Chat
    }

    // TODO: Get from authentication/user context
    private string CurrentUserId { get; set; } = "player-1";

    private bool IsInCombat => GameState?.CurrentCombat is { Status: CombatStatus.Active };

    private Character? CurrentTurnCharacter =>
        GameState?.Characters.FirstOrDefault(c => c.Id == Campaign?.CurrentTurnCharacterId);

    protected override async Task OnInitializedAsync()
    {
        //HubService.OnDiceRollResult += HandleDiceRollResult;
        await base.OnInitializedAsync();
        try
        {
            var user = await AuthStateProvider.GetAuthenticationStateAsync();
            if (string.IsNullOrEmpty(user.User.Identity?.Name)) throw new Exception("User not authenticated");
            CurrentUserId = user.User.Identity?.Name ?? "player-1";
            // Load campaign
            Campaign = await CampaignService.GetCampaignByIdAsync(CampaignId);
            if (Campaign == null)
            {
                ErrorMessage = "Campaign not found.";
                return;
            }

            // Load current player's character - use CharacterId if provided, otherwise find by PlayerId
            var characters = await CharacterService.GetCharactersByCampaignAsync(CampaignId);
            CurrentCharacter = !string.IsNullOrEmpty(CharacterId) ? characters.FirstOrDefault(c => c.Id == CharacterId) : characters.FirstOrDefault(c => c.PlayerId == CurrentUserId);

            if (CurrentCharacter is null && !string.IsNullOrEmpty(CharacterId))
            {
                CurrentCharacter = await CharacterService.GetCharacterByIdAsync(CharacterId);
                //ErrorMessage = "You don't have a character in this campaign.";
                //return;
            }

            if (CurrentCharacter is null)
            {
                ErrorMessage = "You don't have a character in this campaign.";
                return;
            }

            // Connect to SignalR hub
            await HubService.StartAsync();
            Track(HubService.OnMessageUpdated(HandleRuntimeMessage));
            Track(HubService.OnRecovered(HandleRecovered));

            // Subscribe to all game events
            Track(HubService.OnStateUpdated(HandleStateUpdated));
            Track(HubService.OnNarrativeAdded(HandleNarrativeAdded));
            Track(HubService.OnCombatStarted(HandleCombatStarted));
            Track(HubService.OnTurnChanged(HandleTurnChanged));

            // Join campaign
            var recovery = await HubService.JoinCampaignAsync(
                CampaignId,
                CurrentUserId,
                CurrentCharacter.Id,
                CurrentCharacter.Name, ScopeLeaseId
            );
            HandleRecovered(recovery);

            // Load complete game state from server
            if (recovery.GameState is not null) HandleStateUpdated(recovery.GameState);
            if (GameState == null)
            {
                ErrorMessage = "Failed to load game state.";
                return;
            }
            if (Campaign?.OwnerId == CurrentUserId && !recovery.HasAcceptedRequests && recovery.Messages.Count == 0)
                await HubService.SendMessageAsync(CampaignId, CurrentUserId, "Introduce yourself and either summarize the current game-state, or begin the new campaign by describing the world.", clientMessageId: $"intro-{CampaignId}");
            SubmissionConfirmed = true;
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Joined game for campaign {CampaignId} as character {CharacterId}", CampaignId, CurrentCharacter.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize game");
            ErrorMessage = "Unable to load game. Reload state and try again.";
        }
    }
















    private void HandleStateUpdated(GameState newState)
    {
        if (IsDisposed || newState.CampaignId != CampaignId) return;
        Logger.LogInformation("Game state updated");

        // Update game state from server
        if (GameState is not null && GameState.Version > newState.Version) return;
        GameState = newState;
        Campaign = newState.Campaign;
        Logger.LogInformation($"Game state updated: {JsonSerializer.Serialize(GameState)}");
        // Update current character from the updated game state
        // First try by character ID if we have it
        if (!string.IsNullOrEmpty(CharacterId))
        {
            CurrentCharacter = GameState.Characters.FirstOrDefault(c => c.Id == CharacterId);
        }

        // If not found by ID, try by player ID
        if (CurrentCharacter == null)
        {
            CurrentCharacter = GameState.Characters.FirstOrDefault(c => c.PlayerId == CurrentUserId);
        }

        // Log warning if we still can't find the character
        if (CurrentCharacter == null)
        {
            Logger.LogWarning("Could not find current character after state update. CharacterId: {CharacterId}, PlayerId: {PlayerId}, Characters in state: {Count}",
                CharacterId, CurrentUserId, GameState.Characters.Count);
        }

        // Update combat if exists
        if (Campaign?.CurrentCombatId != null)
        {
            CurrentCombatEncounter = GameState.CurrentCombat;

            // Auto-switch to combat tab if combat started
            if (IsInCombat && ActiveTab != "combat")
            {
                ActiveTab = "combat";
            }
        }
        //else
        //{
        //    ActiveTab = "story";
        //}

        InvokeAsync(StateHasChanged);
    }

    private void HandleNarrativeAdded(Narrative narrative)
    {
        Logger.LogInformation("Narrative added: {NarrativeType}", narrative.Type);

        // Add narrative to game state
        if (GameState != null)
        {
            if (GameState.RecentNarratives.All(n => n.Id != narrative.Id)) GameState.RecentNarratives.Add(narrative);

            // If on story tab, refresh to show new narrative
            InvokeAsync(StateHasChanged);
        }
    }

    private void HandleCombatStarted(CombatEncounter encounter)
    {
        Logger.LogInformation("Combat started: {EncounterId}", encounter.Id);

        CurrentCombatEncounter = encounter;

        // Auto-switch to combat tab
        ActiveTab = "combat";

        // Add combat notification to chat
        //ChatMessages.Add(new ChatMessage
        //{
        //    Id = Guid.NewGuid().ToString(),
        //    Content = $"⚔️ **Combat has begun!**",
        //    IsUser = false,
        //    Timestamp = DateTime.UtcNow,
        //    PlayerName = "Game Master"
        //});

        InvokeAsync(StateHasChanged);
    }

    private void HandleTurnChanged(string characterId, string characterName)
    {
        Logger.LogInformation("Turn changed to: {CharacterName}", characterName);

        if (Campaign != null)
        {
            Campaign.CurrentTurnCharacterId = characterId;
        }

        // Notify whose turn it is
        var isYourTurn = characterId == CurrentCharacter?.Id;
        ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Content = isYourTurn ?
                "🎲 **It's your turn!** What do you do?" :
                $"⏳ It's **{characterName}'s** turn.",
            IsUser = false,
            Timestamp = DateTime.UtcNow,
            PlayerName = "Game Master"
        });

        InvokeAsync(StateHasChanged);
    }

    private async void HandleRuntimeMessage(RuntimeMessage message)
    {
        if (IsDisposed || message.ScopeId != CampaignId) return;
        try
        {
            await InvokeAsync(() =>
            {
                if (IsDisposed) return;
                ObserveRuntime(message, CurrentUserId);
                var changed = _runtimeChat.Apply(ChatMessages, message, CurrentUserId, CurrentCharacter?.Name);
                if (changed && CurrentMobileView != MobileView.Chat && !string.IsNullOrEmpty(message.Content)) _unreadResponses.Add(message.Id);
                IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(CampaignId ?? string.Empty, CurrentUserId);
                StateHasChanged();
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Response refresh failed"); }
    }

    private async void HandleRecovered(ScopeRecoverySnapshot snapshot)
    {
        if (IsDisposed || snapshot.ScopeId != CampaignId) return;
        try
        {
            await InvokeAsync(() =>
            {
                if (IsDisposed) return;
                if (snapshot.GameState is not null) HandleStateUpdated(snapshot.GameState);
                RememberRecovery(snapshot);
                foreach (var message in snapshot.Messages)
                {
                    ObserveRuntime(message, CurrentUserId);
                    _runtimeChat.Apply(ChatMessages, message, CurrentUserId, CurrentCharacter?.Name);
                }
                IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(CampaignId ?? string.Empty, CurrentUserId);

                StateHasChanged();
            });
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Session recovery failed"); }
    }

    private Task SendMessage(string message) => SendMessageCore(message);
    private Task RetryMessage(ChatMessage message) => SendMessageCore(message.Content, message.IsAuthoritative || message.WasRejected ? null : message.ClientMessageId, message.ActionIntent);
    private Task CancelResponse() => CancelPending(_runtimeChat, CampaignId!, CurrentUserId);

    private async Task SendMessageCore(string message, string? retryId = null, PlayerActionIntent? intent = null)
    {
        SubmissionConfirmed = false;
        if (string.IsNullOrWhiteSpace(message) || IsProcessing || ReviewRequired || !HubConnected)
            return;

        IsProcessing = true;

        var clientMessageId = retryId ?? Guid.NewGuid().ToString();
        var chatMessage = new ChatMessage
        {
            Id = clientMessageId,
            ClientMessageId = clientMessageId,
            Content = message,
            ActionIntent = intent,
            IsUser = true,
            Timestamp = DateTime.UtcNow,
            PlayerId = CurrentUserId,
            PlayerName = "You",
            Status = MessageProcessingStatus.Queued,
            QueuePosition = 0
        };

        ChatMessages.RemoveAll(m => m.Id == clientMessageId);
        ChatMessages.Add(chatMessage);
        _messageLookup[clientMessageId] = chatMessage;

        StateHasChanged();

        try
        {
            // Send message to Game Master Agent via SignalR
            if (intent is null) await HubService.SendMessageAsync(CampaignId, CurrentUserId, message, AgentType.None, clientMessageId);
            else await HubService.SendActionAsync(CampaignId, CurrentUserId, message, intent, clientMessageId);
            SubmissionConfirmed = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending message");
            chatMessage.Status = MessageProcessingStatus.Failed;
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
            IsProcessing = _runtimeChat.HasPending(CurrentUserId) || HubService.HasPending(CampaignId ?? string.Empty, CurrentUserId);
            StateHasChanged();
        }
    }

    private async Task HandleCharacterUpdated(Character updatedCharacter)
    {
        try
        {
            await CharacterService.UpdateCharacterAsync(updatedCharacter);
            CurrentCharacter = updatedCharacter;
            Logger.LogInformation("Character updated: {CharacterId}", updatedCharacter.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update character");
            ErrorMessage = "Unable to save character changes. Reload state and try again.";
        }
    }

    private void HandleMessageDeleted(ChatMessage message)
    {
        if (message != null)
        {
            ChatMessages.Remove(message);
            if (!string.IsNullOrEmpty(message.ClientMessageId))
            {
                _messageLookup.Remove(message.ClientMessageId);
            }

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

    private void SetActiveTab(string tabName)
    {
        ActiveTab = tabName;
        StateHasChanged();
    }

    private void TogglePartyView()
    {
        ShowPartyView = !ShowPartyView;
        StateHasChanged();
    }

    private void SetMobileView(MobileView view)
    {
        CurrentMobileView = view;
        if (view == MobileView.Chat) _unreadResponses.Clear();
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

    private async Task LeaveCampaign()
    {
        try
        {
            await HubService.LeaveCampaignAsync(CampaignId, CurrentUserId, CurrentCharacter!.Id);
            NavManager.NavigateTo("/startup");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error leaving campaign");
            ErrorMessage = "Unable to leave campaign. Reload state and try again.";
        }
    }

    private string GetPlayerName(string playerId)
    {
        // Get player name from character list
        var character = GameState?.Characters.FirstOrDefault(c => c.PlayerId == playerId || c.Name == playerId);
        return character?.Name ?? "Game Master";
    }

    public override async ValueTask DisposeAsync()
    {
        DisposeSubscriptions();
        try { if (!string.IsNullOrEmpty(CampaignId)) await HubService.DetachScopeAsync(CampaignId, ScopeLeaseId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Scope detach failed"); }
        finally { await base.DisposeAsync(); }
    }
}
