using AgenticRpg.Client.Services;
using AgenticRpg.Core.Agents.Llms;
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
    private bool ShowCharacterModal { get; set; } = false;
    private Character? ViewingCharacter { get; set; }
    private IReadOnlyList<string> AvailableModels { get; set; } = OpenRouterModels.GetAllModelsFromEmbeddedFile();
    private string? _selectedModel;
    private string _modelFilter = string.Empty;
    private string InvitationCodeInput { get; set; } = string.Empty;
    private string? InvitationCodeMessage { get; set; }
    private string? InvitationCodeError { get; set; }

    private IEnumerable<string> FilteredModels => string.IsNullOrWhiteSpace(_modelFilter)
        ? AvailableModels
        : AvailableModels.Where(model => model.Contains(_modelFilter, StringComparison.OrdinalIgnoreCase));
    private bool CanApplyModelOverride => !string.IsNullOrEmpty(_selectedModel);

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

    private void CreateCampaignFromWorld()
    {
        if (SelectedWorld != null)
        {
            Navigation.NavigateTo($"/campaign-creation?worldId={SelectedWorld.Id}");
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
        ViewingCharacter = character;
        ShowCharacterModal = true;
    }

    private void CloseCharacterModal()
    {
        ShowCharacterModal = false;
        ViewingCharacter = null;
    }

    private bool _showWaitModal;
    private async Task ShowCharacterIntro(Character character)
    {
        if (!string.IsNullOrEmpty(character.IntroVidUrl))
        {

            ViewingCharacter = character;
            _isShowVideoModal = true;
            StateHasChanged();
            return;
        }
        _showWaitModal = true;
        StateHasChanged();
        await Task.Delay(1);
        var characterUpdate = await CharacterService.GenerateCharacterVideo(character.Id);
        ViewingCharacter = characterUpdate;
        _showWaitModal = false;

        _isShowVideoModal = true;
        StateHasChanged();
    }

    private void CloseVideoModal()
    {
        _isShowVideoModal = false;
        ViewingCharacter = null;
    }
}
