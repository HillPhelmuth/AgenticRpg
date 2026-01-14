using AgenticRpg.Client.Services;
using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
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
    private List<World> Worlds { get; set; } = [];
    private List<Character> AllPlayerCharacters { get; set; } = [];
    private Campaign? SelectedCampaign { get; set; }
    private World? SelectedWorld { get; set; }
    private Character? SelectedCharacter { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }
    private string? ModelSelectionMessage { get; set; }
    private string? ModelSelectionError { get; set; }

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
    private IReadOnlyList<string> AvailableModels { get; set; } = Array.Empty<string>();
    private string? _selectedModel;
    private string _modelFilter = string.Empty;

    private IEnumerable<string> FilteredModels => string.IsNullOrWhiteSpace(_modelFilter)
        ? AvailableModels
        : AvailableModels.Where(model => model.Contains(_modelFilter, StringComparison.OrdinalIgnoreCase));
    private bool CanApplyModelOverride => !string.IsNullOrEmpty(_selectedModel);

    protected override async Task OnInitializedAsync()
    {
        AvailableModels = OpenRouterModels.GetAllModelsFromEmbeddedFile();
        await LoadData();
    }

    private async Task SelectModel()
    {
        ModelSelectionMessage = null;
        ModelSelectionError = null;

        if (string.IsNullOrEmpty(_selectedModel))
        {
            ModelSelectionError = "Select a model before applying.";
            return;
        }

        var campaignId = SelectedCampaign?.Id;
        var campaignName = SelectedCampaign?.Name;
        var scopeDescription = SelectedCampaign != null
            ? $"campaign {campaignName}"
            : "all standalone sessions";

        try
        {
            await HubService.StartAsync();
            await HubService.ChangeModelAsync(campaignId, _selectedModel);
            ModelSelectionMessage = SelectedCampaign != null
                ? $"Applied {_selectedModel} to {campaignName}."
                : $"Applied {_selectedModel} as default for future sessions.";
            Logger.LogInformation("Player {PlayerId} set model {Model} for {Scope}", CurrentUserId, _selectedModel, scopeDescription);
        }
        catch (Exception ex)
        {
            ModelSelectionError = "Failed to apply model. Please try again.";
            Logger.LogError(ex, "Failed to set model override {Model} for {Scope}", _selectedModel, scopeDescription);
        }
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
            // Load all campaigns
            var allCampaigns = await CampaignService.GetAllCampaignsAsync();
            Campaigns = allCampaigns.ToList();
            
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
        ModelSelectionMessage = null;
        ModelSelectionError = null;
    }

    private void SelectWorld(World world)
    {
        SelectedWorld = world;
        SelectedCampaign = null;
        SelectedCharacter = null;
        ModelSelectionMessage = null;
        ModelSelectionError = null;
    }

    private void SelectCharacter(Character character)
    {
        SelectedCharacter = character;
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
}
