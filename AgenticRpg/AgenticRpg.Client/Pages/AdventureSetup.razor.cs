using AgenticRpg.Client.Services;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace AgenticRpg.Client.Pages;

public partial class AdventureSetup
{
    [SupplyParameterFromQuery] public string? WorldId { get; set; }
    [SupplyParameterFromQuery] public string? CampaignId { get; set; }
    [Inject] private IWorldService Worlds { get; set; } = default!;
    [Inject] private ICharacterService Characters { get; set; } = default!;
    [Inject] private ICampaignService Campaigns { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    private List<World> _worlds = [];
    private List<Character> _characters = [];
    private string? _worldId, _characterId, _error;
    private bool _loading = true, _busy;
    protected override async Task OnParametersSetAsync() => await Load();
    private async Task Load()
    {
        _loading = true; _error = null;
        try
        {
            var account = (await Authentication.GetAuthenticationStateAsync()).User.Identity?.Name ?? throw new InvalidOperationException("Sign in first.");
            _worlds = (await Worlds.GetAllWorldsAsync()).ToList();
            _characters = (await Characters.GetCharactersByPlayerAsync(account)).ToList();
            if (WorldId is not null && await Worlds.GetWorldByIdAsync(WorldId) is null) throw new InvalidOperationException("That world isn't available.");
            if (CampaignId is not null && await Campaigns.GetCampaignByIdAsync(CampaignId) is null) throw new InvalidOperationException("That campaign isn't available.");
        }
        catch (Exception) { _error = "Couldn't load your adventure setup. Retry, stubborn mortal."; }
        finally { _loading = false; }
    }
    private static string Esc(string value) => Uri.EscapeDataString(value);
    private void UseWorld() => Navigation.NavigateTo($"/adventure-setup?worldId={Esc(_worldId!)}");
    private void CreateWorld(string? starter) => Navigation.NavigateTo($"/campaign-creation?worldOnly=true&journey=true{(starter is null ? "" : $"&starter={Esc(starter)}")}");
    private void CreateCampaign() => Navigation.NavigateTo($"/campaign-creation?worldOnly=false&journey=true&worldId={Esc(WorldId!)}");
    private void CreateCharacter(string? starter) => Navigation.NavigateTo($"/character-creation/{Esc(CampaignId!)}?journey=true{(starter is null ? "" : $"&starter={Esc(starter)}")}");
    private async Task UseCharacter()
    {
        if (_busy || string.IsNullOrEmpty(_characterId)) return;
        _busy = true;
        try
        {
            var character = _characters.First(c => c.Id == _characterId);
            character.CampaignId = CampaignId!;
            await Characters.UpdateCharacterAsync(character);
            Navigation.NavigateTo($"/lobby?campaignId={Esc(CampaignId!)}&characterId={Esc(character.Id)}&journey=true");
        }
        catch (Exception) { _error = "Couldn't assign your character. Retry before blaming the dice."; }
        finally { _busy = false; }
    }
}
