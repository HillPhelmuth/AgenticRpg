using AgenticRpg.Client.Services;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AgenticRpg.Client.Pages;

public partial class CharacterLobby : IAsyncDisposable
{
    [Parameter]
    [SupplyParameterFromQuery(Name = "campaignId")]
    public string CampaignId { get; set; } = string.Empty;

    [Parameter]
    [SupplyParameterFromQuery(Name = "characterId")]
    public string? CharacterId { get; set; }

    [Inject] private new ICampaignService CampaignService { get; set; } = default!;
    [Inject] private ICharacterService CharacterService { get; set; } = default!;
    [Inject] private new IGameHubService HubService { get; set; } = default!;
    [Inject] private ILogger<CharacterLobby> Logger { get; set; } = default!;
    [Inject] private NavigationManager NavManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    private Campaign? Campaign { get; set; }
    private List<Character> Characters { get; set; } = [];
    private Character? CurrentCharacter { get; set; }
    private bool IsProcessing { get; set; }
    private string? ErrorMessage { get; set; }
    private bool IsReady { get; set; }
    private bool AllPlayersReady { get; set; }
    private bool IsStarting { get; set; }
    private bool IsConnected { get; set; }
    private Dictionary<string, PlayerReadyStatus> ReadyStatuses { get; set; } = new();
    private string _currentUserId = "player-1"; // TODO: Replace with authenticated user context

    // TODO: Get from authentication/user context
    private string CurrentUserId => _currentUserId;

    private int ReadyCount => ReadyStatuses.Values.Count(status => status.IsReady);
    private int TotalPlayers => Characters.Count;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var user = await AuthStateProvider.GetAuthenticationStateAsync();
            if (string.IsNullOrEmpty(user.User.Identity?.Name)) throw new Exception("User not authenticated");
            _currentUserId = user.User.Identity?.Name ?? "player-1";
            Campaign = await CampaignService.GetCampaignByIdAsync(CampaignId);
            if (Campaign == null)
            {
                ErrorMessage = "Campaign not found.";
                return;
            }

            // Load all characters in the campaign
            Characters = (await CharacterService.GetCharactersByCampaignAsync(CampaignId)).ToList();

            // Find current player's character - use CharacterId if provided, otherwise find by PlayerId
            if (!string.IsNullOrEmpty(CharacterId))
            {
                CurrentCharacter = Characters.FirstOrDefault(c => c.Id == CharacterId);
            }
            else
            {
                CurrentCharacter = Characters.FirstOrDefault(c => c.PlayerId == CurrentUserId);
            }
            CurrentCharacter ??= await CharacterService.GetCharacterByIdAsync(CharacterId);
            if (Characters.All(x => x.Id != CurrentCharacter?.Id))
            {
                Characters.Add(CurrentCharacter);
            }
            // Connect to SignalR hub
            await HubService.StartAsync();
            await HubService.JoinCampaignAsync(
                Campaign.Id,
                CurrentUserId,
                CurrentCharacter.Id,
                CurrentCharacter.Name
            );
            // Subscribe to lobby events
            HubService.OnPlayerJoined(HandlePlayerJoined);
            HubService.OnPlayerLeft(HandlePlayerLeft);
            HubService.OnReadyStatusUpdated(HandleReadyStatusUpdated);
            HubService.OnReadyStatusSnapshot(HandleReadyStatusSnapshot);
            HubService.OnAllPlayersReady(HandleAllPlayersReady);
            HubService.OnCampaignStarted(HandleCampaignStarted);
            HubService.OnStateUpdated(HandleStateUpdated);

            // Join campaign lobby
            if (CurrentCharacter != null)
            {
                
                IsConnected = true;

                // Check if already ready
                if (ReadyStatuses.TryGetValue(CurrentUserId, out var readyStatus))
                {
                    IsReady = readyStatus.IsReady;
                }
            }

            Logger.LogInformation("Joined lobby for campaign {CampaignId}", CampaignId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize lobby");
            ErrorMessage = $"Failed to load lobby: {ex.Message}";
        }
    }

    private void HandleStateUpdated(GameState gameState)
    {
        Logger.LogInformation("Game state updated");
        InvokeAsync(() =>
        {
            // Update ready statuses
            ReadyStatuses = gameState.PlayerReadyStatuses ?? new Dictionary<string, PlayerReadyStatus>();
            if (ReadyStatuses.TryGetValue(CurrentUserId, out var readyValue))
            {
                IsReady = readyValue.IsReady;
            }
            //Characters = gameState.Characters;
            StateHasChanged();
        });
    }
    private void HandlePlayerJoined(string playerId, string characterId, string playerName)
    {
        Logger.LogInformation("Player joined: {PlayerName}", playerName);

        // Reload characters to show new player
        InvokeAsync(async () =>
        {
            UpsertReadyStatus(playerId, false, characterId, playerName);
            Characters = (await CharacterService.GetCharactersByCampaignAsync(CampaignId)).ToList();
            StateHasChanged();
        });
    }

    private void HandlePlayerLeft(string playerId)
    {
        Logger.LogInformation("Player left: {PlayerId}", playerId);

        // Reload characters
        InvokeAsync(async () =>
        {
            ReadyStatuses.Remove(playerId);
            //await HubService.LeaveCampaignAsync(CampaignId);
            Characters = (await CharacterService.GetCharactersByCampaignAsync(CampaignId)).Where(x => x.PlayerId != playerId).ToList();
            StateHasChanged();
        });
    }

    private void HandleReadyStatusUpdated(string playerId, bool isReady)
    {
        Logger.LogInformation("Player {PlayerId} ready status: {IsReady}", playerId, isReady);
        InvokeAsync(() =>
        {
            UpsertReadyStatus(playerId, isReady);
            if (playerId == CurrentUserId)
            {
                IsReady = isReady;
            }
            StateHasChanged();
        });
    }

    private void HandleReadyStatusSnapshot(Dictionary<string, PlayerReadyStatus> snapshot)
    {
        InvokeAsync(() =>
        {
            ReadyStatuses = snapshot?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, PlayerReadyStatus>();
            if (ReadyStatuses.TryGetValue(CurrentUserId, out var readyValue))
            {
                IsReady = readyValue.IsReady;
            }
            StateHasChanged();
        });
    }

    private void HandleAllPlayersReady(bool allReady)
    {
        Logger.LogInformation("All players ready: {AllReady}", allReady);
        AllPlayersReady = allReady;
        InvokeAsync(StateHasChanged);
    }

    private void HandleCampaignStarted(string campaignId)
    {
        Logger.LogInformation("Campaign started: {CampaignId}", campaignId);

        // Navigate to game page
        InvokeAsync(() =>
        {
            NavManager.NavigateTo($"/game?campaignId={campaignId}&characterId={CurrentCharacter?.Id}");
        });
    }

    private async Task ToggleReady()
    {
        if (CurrentCharacter == null || Campaign == null)
            return;

        IsProcessing = true;
        try
        {
            IsReady = !IsReady;
            UpsertReadyStatus(CurrentUserId, IsReady, CurrentCharacter?.Id, CurrentCharacter?.Name);
            await HubService.UpdateReadyStatusAsync(CampaignId, CurrentUserId, IsReady);
            Logger.LogInformation("Updated ready status to {IsReady}", IsReady);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update ready status");
            ErrorMessage = $"Failed to update ready status: {ex.Message}";
            IsReady = !IsReady; // Revert on error
            UpsertReadyStatus(CurrentUserId, IsReady, CurrentCharacter?.Id, CurrentCharacter?.Name);
        }
        finally
        {
            IsProcessing = false;
            StateHasChanged();
        }
    }

    private void UpsertReadyStatus(string playerId, bool isReady, string? characterId = null, string? playerName = null)
    {
        if (!ReadyStatuses.TryGetValue(playerId, out var status))
        {
            var resolvedCharacterId = characterId ?? CurrentCharacter?.Id ?? string.Empty;
            var resolvedPlayerName = playerName ??
                (playerId == CurrentUserId ? (CurrentCharacter?.Name ?? playerId) : playerId);

            status = new PlayerReadyStatus
            {
                PlayerId = playerId,
                CharacterId = resolvedCharacterId,
                PlayerName = resolvedPlayerName
            };
            ReadyStatuses[playerId] = status;
        }

        status.IsReady = isReady;
        status.ReadyAt = isReady ? DateTime.UtcNow : null;
    }

    private void CreateCharacter()
    {
        NavManager.NavigateTo($"/character-creation/{CampaignId}");
    }

    private async Task GoBack()
    {
        await HubService.LeaveCampaignAsync(CampaignId, _currentUserId, CharacterId!);
        NavManager.NavigateTo("/startup");

    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsConnected)
            {
                await HubService.LeaveCampaignAsync(CampaignId, _currentUserId, CharacterId!);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during disposal");
        }
    }
}
