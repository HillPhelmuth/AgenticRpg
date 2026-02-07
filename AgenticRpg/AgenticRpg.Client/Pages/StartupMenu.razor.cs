using AgenticRpg.Client.Services;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AgenticRpg.Client.Pages;

public partial class StartupMenu
{
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
    private Character? _viewingCharacter;

    private World? _viewingWorld;
    private string InvitationCodeInput { get; set; } = string.Empty;
    private string? InvitationCodeMessage { get; set; }
    private string? InvitationCodeError { get; set; }

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

    private void CreateCampaignFromWorld(string? worldId = null)
    {
        if (SelectedWorld != null)
        {
            worldId ??= SelectedWorld.Id;
            Navigation.NavigateTo($"/campaign-creation?worldId={worldId}");
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
        if (SelectedCampaign != null && SelectedCharacter != null)
        {
            Navigation.NavigateTo($"/game?campaignId={SelectedCampaign.Id}&characterId={SelectedCharacter.Id}");
        }
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
}
