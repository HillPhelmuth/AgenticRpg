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
    [Inject] private AudioInterop AudioInterop { get; set; } = default!;

    private Campaign? Campaign { get; set; }
    private Character? CurrentCharacter { get; set; }
    private GameState? GameState { get; set; }
    private CombatEncounter? CurrentCombatEncounter { get; set; }
    private List<ChatMessage> ChatMessages { get; set; } = [];
    private readonly Dictionary<string, ChatMessage> _messageLookup = new(StringComparer.OrdinalIgnoreCase);
    private bool IsProcessing { get; set; }
    private string? ErrorMessage { get; set; }
    private bool IsConnected { get; set; }
    private string ActiveTab { get; set; } = "story";
    private bool ShowPartyView { get; set; }
    private new UserInput? UserInputRef { get; set; }
    private MobileView CurrentMobileView { get; set; } = MobileView.Game;
    private CancellationTokenSource? _speechCts;
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

            if (CurrentCharacter == null)
            {
                CurrentCharacter = await CharacterService.GetCharacterByIdAsync(CharacterId);
                //ErrorMessage = "You don't have a character in this campaign.";
                //return;
            }

            // Connect to SignalR hub
            await HubService.StartAsync();

            // Subscribe to all game events
            HubService.OnReceiveMessage(HandleReceiveMessage);
            HubService.OnStateUpdated(HandleStateUpdated);
            HubService.OnNarrativeAdded(HandleNarrativeAdded);
            HubService.OnCombatStarted(HandleCombatStarted);
            HubService.OnTurnChanged(HandleTurnChanged);
            HubService.OnMessageStatusChanged(HandleMessageStatusChanged);

            // Join campaign
            await HubService.JoinCampaignAsync(
                CampaignId,
                CurrentUserId,
                CurrentCharacter.Id,
                CurrentCharacter.Name
            );
            IsConnected = true;

            // Load complete game state from server
            GameState = await GameStateService.GetGameStateAsync(CampaignId);
            if (GameState == null)
            {
                ErrorMessage = "Failed to load game state.";
                return;
            }
            if (Campaign?.OwnerId == CurrentUserId)
                await HubService.SendMessageAsync(CampaignId, CurrentUserId, "Introduce yourself and either summarize the current game-state, or begin the new campaign by describing the world.");
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogInformation("Joined game for campaign {CampaignId} as character {CharacterId}", CampaignId, CurrentCharacter.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize game");
            ErrorMessage = $"Failed to load game: {ex.Message}";
        }
    }


    private void HandleReceiveMessage(string playerId, AgentResponse message, DateTime timestamp)
    {
        if (playerId == CurrentUserId)
        {
            // Already rendered locally
            return;
        }

        // Receive messages from Game Master or other players
        ChatMessages.Add(new ChatMessage
        {
            Id = message.Id,
            Content = message.FormattedResponse?.MessageToPlayers ?? message.Error ?? message.Message,
            IsUser = playerId == CurrentUserId,
            Timestamp = timestamp,
            PlayerId = playerId,
            PlayerName = playerId == CurrentUserId ? "You" : GetPlayerName(playerId),
            SuggestedActions = message.FormattedResponse?.SuggestedActions?.FirstOrDefault(x => x.Player.Equals(CurrentCharacter?.Name, StringComparison.OrdinalIgnoreCase))?.Suggestions ?? []
        });

        //_ = PlaySpeechAsync(message.FormattedResponse?.MessageToPlayers ?? message.Error ?? message.Message);
        InvokeAsync(StateHasChanged);
    }

    

    private void HandleMessageStatusChanged(string messageId, string status, int? position, string? note)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return;
        }

        if (!_messageLookup.TryGetValue(messageId, out var existingMessage))
        {
            return;
        }

        if (!Enum.TryParse<MessageProcessingStatus>(status, out var parsedStatus))
        {
            return;
        }

        existingMessage.Status = parsedStatus;
        existingMessage.QueuePosition = position;
        existingMessage.StatusNote = note;
        InvokeAsync(StateHasChanged);
    }

    private void HandleStateUpdated(GameState newState)
    {
        Logger.LogInformation("Game state updated");

        // Update game state from server
        GameState = newState;
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
            GameState.RecentNarratives.Add(narrative);

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

    private async Task SendMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        IsProcessing = true;

        var clientMessageId = Guid.NewGuid().ToString();
        var chatMessage = new ChatMessage
        {
            Id = clientMessageId,
            ClientMessageId = clientMessageId,
            Content = message,
            IsUser = true,
            Timestamp = DateTime.UtcNow,
            PlayerId = CurrentUserId,
            PlayerName = "You",
            Status = MessageProcessingStatus.Queued,
            QueuePosition = 0
        };

        ChatMessages.Add(chatMessage);
        _messageLookup[clientMessageId] = chatMessage;

        StateHasChanged();

        try
        {
            // Send message to Game Master Agent via SignalR
            await HubService.SendMessageAsync(CampaignId, CurrentUserId, message, AgentType.None, clientMessageId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending message");
            chatMessage.Status = MessageProcessingStatus.Failed;
            chatMessage.StatusNote = ex.Message;
            ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Error communicating with Game Master: {ex.Message}",
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
            ErrorMessage = $"Failed to save character changes: {ex.Message}";
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
        StateHasChanged();
    }

    private async void LeaveCampaign()
    {
        try
        {
            await HubService.LeaveCampaignAsync(CampaignId, CurrentUserId, CharacterId!);
            NavManager.NavigateTo("/startup");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error leaving campaign");
            ErrorMessage = $"Failed to leave campaign: {ex.Message}";
        }
    }

    private string GetPlayerName(string playerId)
    {
        // Get player name from character list
        var character = GameState?.Characters.FirstOrDefault(c => c.PlayerId == playerId);
        return character?.Name ?? "Game Master";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsConnected)
            {
                await HubService.LeaveCampaignAsync(CampaignId, CurrentUserId, CharacterId!);
            }

            _speechCts?.Cancel();
            _speechCts?.Dispose();
            await AudioInterop.StopAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during disposal");
        }
    }
}
