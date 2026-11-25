using Microsoft.JSInterop;
using AgenticRpg.Core.Models.Enums;
using System.Net.Http.Json;
using AgenticRpg.Client.Services;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AgenticRpg.Client.Pages;

public partial class StartupMenu
{
    [Inject] private Microsoft.JSInterop.IJSRuntime Browser { get; set; } = default!;
    private bool _showJoinCode;
    private bool _continuing;
    private bool _continueSelection;
    private string? _continueCampaignId, _continueCharacterId;
    private async Task ContinueSelected()
    {
        SelectedCampaign = Campaigns.FirstOrDefault(c => c.Id == _continueCampaignId);
        SelectedCharacter = AllPlayerCharacters.FirstOrDefault(c => c.Id == _continueCharacterId && (string.IsNullOrEmpty(c.CampaignId) || c.CampaignId == _continueCampaignId));
        await JoinCampaignWithCharacter();
    }
    private void CreateJoiningCharacter() => Navigation.NavigateTo($"/adventure-setup?campaignId={Uri.EscapeDataString(_continueCampaignId!)}");
    private void StartNewAdventure() => Navigation.NavigateTo("/adventure-setup");
    private async Task ContinueAdventure()
    {
        if (_continuing) return;
        _continuing = true;
        try
        {
            await using var module = await Browser.InvokeAsync<Microsoft.JSInterop.IJSObjectReference>("import", ["./_content/AgenticRpg.Components/ChatComponents/draftInput.js"]);
            var recent = await module.InvokeAsync<string>("get", $"rpg-draft:{Uri.EscapeDataString(CurrentUserId)}:last-adventure");
            var choice = !string.IsNullOrEmpty(recent) ? System.Text.Json.JsonSerializer.Deserialize<RecentAdventure>(recent) : null;
            var character = AllPlayerCharacters.FirstOrDefault(c => choice is not null && c.Id == choice.CharacterId && c.CampaignId == choice.CampaignId)
                ?? AllPlayerCharacters.Where(c => Campaigns.Any(p => p.Id == c.CampaignId)).OrderByDescending(c => Campaigns.First(p => p.Id == c.CampaignId).StartedAt ?? Campaigns.First(p => p.Id == c.CampaignId).CreatedAt).FirstOrDefault();
            if (character is not null && await CampaignService.GetCampaignByIdAsync(character.CampaignId) is { } campaign)
                Navigation.NavigateTo($"/{(campaign.StartedAt is null ? "lobby" : "game")}?campaignId={Uri.EscapeDataString(campaign.Id)}&characterId={Uri.EscapeDataString(character.Id)}");
            else { _continueSelection = true; _continueCampaignId = Campaigns.FirstOrDefault()?.Id; }
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Continue failed"); ErrorMessage = "Couldn't continue. Reload your adventures and try again."; }
        finally { _continuing = false; }
    }
    private sealed record RecentAdventure(string CampaignId, string CharacterId);
    [Inject] private ICharacterService CharacterService { get; set; } = null!;
    [Inject] private IWorldService WorldService { get; set; } = null!;
    [Inject] private ILogger<StartupMenu> Logger { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    private List<Campaign> Campaigns { get; set; } = [];
    private List<Campaign> OwnerCampaigns { get; set; } = [];
    private List<Campaign> InvitationCampaigns { get; set; } = [];
    private List<World> Worlds { get; set; } = [];
    private List<Character> AllPlayerCharacters { get; set; } = [];
    private Campaign? SelectedCampaign { get; set; }
    private World? SelectedWorld { get; set; }
    private Character? SelectedCharacter { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }
    [Inject] private HttpClient DraftHttp { get; set; } = default!;
    private List<DraftSummary> Drafts { get; set; } = [];
    private void ResumeDraft(DraftSummary draft) => Navigation.NavigateTo($"/{(draft.SessionType == SessionType.CharacterCreation ? "character-creation" : "campaign-creation")}?sessionId={Uri.EscapeDataString(draft.SessionId)}&worldOnly={draft.SessionType == SessionType.WorldBuilding}");
    private async Task DiscardDraft(DraftSummary draft)
    {
        var response = await DraftHttp.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(draft.SessionId)}");
        if (response.IsSuccessStatusCode) Drafts.Remove(draft);
        else ErrorMessage = "Draft could not be discarded. Wait for any pending action and try again.";
    }
    private bool _isShowVideoModal;

    // Characters available for the selected campaign (not in campaign or in this specific campaign)
    private List<Character> AvailableCharactersForCampaign =>
        AllPlayerCharacters
            //.Where(c => string.IsNullOrEmpty(c.CampaignId) || c.CampaignId == SelectedCampaign?.Id)
            .ToList();

    // Placeholder for current user ID - in real app, would come from auth
    private string CurrentUserId { get; set; } = "player-1"; // TODO: Replace with actual authentication

    // Modal state
    private bool _showCharacterModal;
    private bool _showWorldModal;
    private bool _showDeleteConfirmationModal;
    private bool _isDeleteInProgress;
    private Character? _viewingCharacter;
    private Character? _characterPendingDeletion;

    private World? _viewingWorld;
    private Campaign? _campaignPendingDeletion;
    private string? _deleteConfirmationError;
    private string InvitationCodeInput { get; set; } = string.Empty;
    private string? InvitationCodeMessage { get; set; }
    private string? InvitationCodeError { get; set; }
    private string DeleteConfirmationTitle => _characterPendingDeletion is not null
        ? $"Delete character {_characterPendingDeletion.Name}?"
        : _campaignPendingDeletion is not null
            ? $"Delete campaign {_campaignPendingDeletion.Name}?"
            : "Confirm deletion";
    private string DeleteConfirmationMessage => _characterPendingDeletion is not null
        ? "This permanently removes the character from Cosmos DB and cannot be undone."
        : _campaignPendingDeletion is not null
            ? "This permanently removes the campaign from Cosmos DB and cannot be undone."
            : string.Empty;
    private string? DeleteConfirmationWarning => _characterPendingDeletion is not null && !string.IsNullOrWhiteSpace(_characterPendingDeletion.CampaignId)
        ? "This character is currently assigned to a campaign."
        : _campaignPendingDeletion is not null && _campaignPendingDeletion.PlayerIds.Any()
            ? $"This campaign currently has {_campaignPendingDeletion.PlayerIds.Distinct().Count()} player slot(s) associated with it."
            : null;

    protected override async Task OnInitializedAsync()
    {
        //AvailableModels = OpenRouterModels.GetAllModelsFromEmbeddedFile();
        await LoadData();
    }


    private async Task LoadData()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var authState = await AuthenticationState.GetAuthenticationStateAsync();
            var user = authState.User;
            var userId = user.Identity?.Name ?? "player-1"; // Fallback for demo purposes
            CurrentUserId = userId;
            Drafts = await DraftHttp.GetFromJsonAsync<List<DraftSummary>>("/api/sessions") ?? [];
            // Load campaigns owned by the active user
            var ownerCampaigns = await CampaignService.GetCampaignsByOwnerAsync(CurrentUserId);
            OwnerCampaigns = ownerCampaigns.ToList();
            MergeCampaigns();

            // Load all worlds
            var allWorlds = await WorldService.GetAllWorldsAsync();
            Worlds = allWorlds.ToList();

            // Load all player's characters
            var allCharacters = await CharacterService.GetCharactersByPlayerAsync(CurrentUserId);
            AllPlayerCharacters = allCharacters.ToList();

            Logger.LogInformation("Loaded {CampaignCount} campaigns, {WorldCount} worlds, and {CharacterCount} characters",
                Campaigns.Count, Worlds.Count, AllPlayerCharacters.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load data: {ex.Message}";
            Logger.LogError(ex, "Error loading campaigns and characters");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task Refresh()
    {
        await LoadCampaigns();
    }
    private async Task LoadCampaigns()
    {
        try
        {
            await LoadData();
            Logger.LogInformation("Reloaded {Count} campaigns", Campaigns.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reload campaigns: {ex.Message}";
            Logger.LogError(ex, "Error reloading campaigns");
        }
    }

    private void SelectCampaign(Campaign campaign)
    {
        SelectedCampaign = campaign;
        SelectedWorld = null;
        SelectedCharacter = null;
        //ModelSelectionMessage = null;
        //ModelSelectionError = null;
    }

    private void SelectWorld(World world)
    {
        SelectedWorld = world;
        SelectedCampaign = null;
        SelectedCharacter = null;
        //ModelSelectionMessage = null;
        //ModelSelectionError = null;
    }

    private void SelectCharacter(Character character)
    {
        SelectedCharacter = character;
    }

    /// <summary>
    /// Adds a campaign to the visible list using an invitation code.
    /// </summary>
    private async Task AddInvitationCampaign()
    {
        InvitationCodeMessage = null;
        InvitationCodeError = null;

        var trimmedCode = InvitationCodeInput?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmedCode))
        {
            InvitationCodeError = "Enter an invitation code.";
            return;
        }

        var campaign = await CampaignService.GetCampaignByInvitationCodeAsync(trimmedCode, CurrentUserId);
        if (campaign is null)
        {
            InvitationCodeError = "No campaign found for that invitation code.";
            return;
        }

        InvitationCampaigns = InvitationCampaigns
            .Where(existing => existing.Id != campaign.Id)
            .ToList();
        InvitationCampaigns.Add(campaign);
        MergeCampaigns();
        SelectCampaign(campaign);
        _continueSelection = true;
        _continueCampaignId = campaign.Id;

        InvitationCodeInput = string.Empty;
        InvitationCodeMessage = $"Added {campaign.Name}.";
    }

    /// <summary>
    /// Merges owner and invitation campaigns while preserving selection where possible.
    /// </summary>
    private void MergeCampaigns()
    {
        // Reason: Keep owner and invited campaigns combined while avoiding duplicates on refresh.
        Campaigns = OwnerCampaigns
            .Concat(InvitationCampaigns)
            .DistinctBy(campaign => campaign.Id)
            .ToList();

        if (SelectedCampaign is not null && Campaigns.All(campaign => campaign.Id != SelectedCampaign.Id))
        {
            SelectedCampaign = null;
            SelectedCharacter = null;
        }
    }

    private void CreateNewCampaign()
    {
        Navigation.NavigateTo($"/campaign-creation?worldOnly={true}");
    }

    private void ModifyCampaignWorld(string? worldId = null)
    {
        if (SelectedWorld == null && worldId == null) return;
        worldId ??= SelectedWorld?.Id;
        Navigation.NavigateTo($"/campaign-creation?worldId={worldId}&worldOnly={true}");
    }

    private void CreateCampaignFromWorld(string? worldId = null)
    {
        if (SelectedWorld != null)
        {
            worldId ??= SelectedWorld.Id;
            Navigation.NavigateTo($"/campaign-creation?worldId={worldId}&worldOnly=false");
        }
    }

    private void CreateNewCharacter()
    {
        // Character creation is now independent - no campaign required
        Navigation.NavigateTo("/character-creation");
    }

    private async Task JoinCampaignWithCharacter()
    {
        if (SelectedCampaign == null || SelectedCharacter == null)
            return;

        try
        {
            // Update character's campaign assignment
            SelectedCharacter.CampaignId = SelectedCampaign.Id;
            await CharacterService.UpdateCharacterAsync(SelectedCharacter);

            Logger.LogInformation("Assigned character {CharacterId} to campaign {CampaignId}",
                SelectedCharacter.Id, SelectedCampaign.Id);

            // Reload character list to reflect changes
            var allCharacters = await CharacterService.GetCharactersByPlayerAsync(CurrentUserId);
            AllPlayerCharacters = allCharacters.ToList();
            foreach (var id in AllPlayerCharacters.Select(c => c.CampaignId).Where(id => !string.IsNullOrEmpty(id)).Distinct())
                if (Campaigns.All(c => c.Id != id) && await CampaignService.GetCampaignByIdAsync(id) is { } joined)
                    InvitationCampaigns.Add(joined);
            MergeCampaigns();

            // Navigate to lobby
            JoinLobby();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to join campaign: {ex.Message}";
            Logger.LogError(ex, "Error joining campaign with character");
        }
    }

    private void JoinLobby()
    {
        Console.WriteLine($"Join Lobby Selected Campaign: {SelectedCampaign?.Name}, Selected Character: {SelectedCharacter?.Name}");
        if (SelectedCampaign != null && SelectedCharacter != null)
        {

            Navigation.NavigateTo($"/lobby?campaignId={SelectedCampaign.Id}&characterId={SelectedCharacter.Id}");
        }
    }

    private void JoinGame()
    {
        JoinLobby();
        //if (SelectedCampaign != null && SelectedCharacter != null)
        //{
        //    Navigation.NavigateTo($"/game?campaignId={SelectedCampaign.Id}&characterId={SelectedCharacter.Id}");
        //}
    }

    private void ShowCharacterSheet(Character character)
    {
        _viewingCharacter = character;
        _showCharacterModal = true;
    }

    private void CloseCharacterModal()
    {
        _showCharacterModal = false;
        _viewingCharacter = null;
    }

    private void ShowWorldDetails(World world)
    {
        _showWorldModal = true;
        _viewingWorld = world;
    }

    private void CloseWorldModal()
    {
        _showWorldModal =false;
        _viewingWorld = null;
    }
    private bool _showWaitModal;
    private async Task ShowCharacterIntro(Character character)
    {
        if (!string.IsNullOrEmpty(character.IntroVidUrl))
        {

            _viewingCharacter = character;
            _isShowVideoModal = true;
            StateHasChanged();
            return;
        }
        _showWaitModal = true;
        StateHasChanged();
        await Task.Delay(1);
        var characterUpdate = await CharacterService.GenerateCharacterVideo(character.Id);
        _viewingCharacter = characterUpdate;
        _showWaitModal = false;

        _isShowVideoModal = true;
        StateHasChanged();
    }

    private void CloseVideoModal()
    {
        _isShowVideoModal = false;
        _viewingCharacter = null;
    }

    /// <summary>
    /// Opens the confirmation modal for character deletion.
    /// </summary>
    private void PromptDeleteCharacter(Character character)
    {
        _campaignPendingDeletion = null;
        _characterPendingDeletion = character;
        _deleteConfirmationError = null;
        _showDeleteConfirmationModal = true;
    }

    /// <summary>
    /// Opens the confirmation modal for campaign deletion.
    /// </summary>
    private void PromptDeleteCampaign(Campaign campaign)
    {
        if (!string.Equals(campaign.OwnerId, CurrentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _characterPendingDeletion = null;
        _campaignPendingDeletion = campaign;
        _deleteConfirmationError = null;
        _showDeleteConfirmationModal = true;
    }

    /// <summary>
    /// Resets the shared deletion confirmation state.
    /// </summary>
    private void CloseDeleteConfirmationModal()
    {
        if (_isDeleteInProgress)
        {
            return;
        }

        _showDeleteConfirmationModal = false;
        _deleteConfirmationError = null;
        _characterPendingDeletion = null;
        _campaignPendingDeletion = null;
    }

    /// <summary>
    /// Confirms and executes the pending delete request.
    /// </summary>
    private async Task ConfirmDeleteAsync()
    {
        if (_isDeleteInProgress)
        {
            return;
        }

        _deleteConfirmationError = null;
        _isDeleteInProgress = true;

        try
        {
            if (_characterPendingDeletion is not null)
            {
                await DeleteCharacterAsync(_characterPendingDeletion);
            }
            else if (_campaignPendingDeletion is not null)
            {
                await DeleteCampaignAsync(_campaignPendingDeletion);
            }

            CloseDeleteConfirmationModal();
        }
        catch (Exception ex)
        {
            _deleteConfirmationError = ex.Message;
            Logger.LogError(ex, "Error deleting startup menu item");
        }
        finally
        {
            _isDeleteInProgress = false;
            CloseDeleteConfirmationModal();
        }
    }

    /// <summary>
    /// Deletes a character and updates local UI state.
    /// </summary>
    private async Task DeleteCharacterAsync(Character character)
    {
        await CharacterService.DeleteCharacterAsync(character.Id);

        AllPlayerCharacters = AllPlayerCharacters
            .Where(existingCharacter => existingCharacter.Id != character.Id)
            .ToList();

        if (SelectedCharacter?.Id == character.Id)
        {
            SelectedCharacter = null;
        }

        if (_viewingCharacter?.Id == character.Id)
        {
            _viewingCharacter = null;
            _showCharacterModal = false;
            _isShowVideoModal = false;
        }

        Logger.LogInformation("Deleted character {CharacterId} from startup menu", character.Id);
    }

    /// <summary>
    /// Deletes an owned campaign and updates local UI state.
    /// </summary>
    private async Task DeleteCampaignAsync(Campaign campaign)
    {
        await CampaignService.DeleteCampaignAsync(campaign.Id);

        OwnerCampaigns = OwnerCampaigns
            .Where(existingCampaign => existingCampaign.Id != campaign.Id)
            .ToList();
        InvitationCampaigns = InvitationCampaigns
            .Where(existingCampaign => existingCampaign.Id != campaign.Id)
            .ToList();
        MergeCampaigns();

        if (SelectedCampaign?.Id == campaign.Id)
        {
            SelectedCampaign = null;
            SelectedCharacter = null;
        }

        Logger.LogInformation("Deleted campaign {CampaignId} from startup menu", campaign.Id);
    }
}
