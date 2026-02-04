using AgenticRpg.Client.Services;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Services;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgenticRpg.Client.Pages;

public partial class Home
{
    [Inject]
    private IConfiguration Configuration { get; set; } = default!;
    [Inject]
    private ICharacterService CharacterService { get; set; } = default!;
    [Inject]
    private HttpClient HttpClient { get; set; } = default!;
    [CascadingParameter]
    private Task<AuthenticationState>? authenticationState { get; set; }
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;
    private const string DiceUrl = "api/dice";
    private const string SessionId = "session-1";
    private string? rollResultJson;
    
    private async Task TriggerRoll()
    {
        await HubService.JoinSessionAsync(SessionId, "test-user", "Character Creator");
        var request = new DiceRollRequest()
        {
            SessionId = SessionId, CampaignId = SessionId, NumberOfDice = 2, NumberOfRollWindows = 2,
            DieType = DieType.D20
        };
        var response = await HttpClient.PostAsJsonAsync(DiceUrl, request);
        if (response.IsSuccessStatusCode)
        {
            var rollResults = await response.Content.ReadFromJsonAsync<List<RollDiceResults>>();
            rollResultJson = JsonSerializer.Serialize(rollResults, new JsonSerializerOptions() { WriteIndented = true });
            StateHasChanged();
            //if (rollResults != null)
            //{
            //    await HubService.Connection.InvokeAsync("SubmitRollResult", rollResults);
            //}
        }
    }
    protected override async Task OnInitializedAsync()
    {
        if (authenticationState is not null)
        {
            var state = await authenticationState;
            if (state.User.Identity.IsAuthenticated)
            {
                NavigationManager.NavigateTo("startup");
            }
        }
        await base.OnInitializedAsync();
    }
    //private async void HandleDiceRollResult(object? sender, List<RollDiceResults> e)
    //{
    //    try
    //    {
    //        //await HubService.SendMessageAsync(SessionId, "test-user", $"Player completed the following rolls:\n\n{JsonSerializer.Serialize(e)}", "CharacterCreation");
    //        await HubService.Connection.InvokeAsync("SubmitRollResult", e);
    //    }
    //    catch (Exception ex)
    //    {
    //        //ToDo add real logging and user notification component.
    //        Console.WriteLine(ex);
    //    }
    //}
    private void StartUp() => Navigation.NavigateTo("/startup");

}