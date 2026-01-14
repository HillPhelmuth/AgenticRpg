using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Hubs;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgenticRpg.Client.Layout;

public abstract class RpgPageBase : ComponentBase
{
    protected UserInput UserInputRef { get; set; } = default!;
    [Inject] 
    protected IGameHubService HubService { get; set; } = default!;
    [Inject] protected ICampaignService CampaignService { get; set; } = default!;

    protected override Task OnInitializedAsync()
    {
        HubService.OnDiceRollResult += HandleDiceRollResult;
        return base.OnInitializedAsync();
    }
    protected virtual async void HandleDiceRollResult(object? sender, List<RollDiceResults> e)
    {
        try
        {
            //await HubService.SendMessageAsync(SessionId, "test-user", $"Player completed the following rolls:\n\n{JsonSerializer.Serialize(e)}", "CharacterCreation");
            //await HubService.Connection.InvokeAsync("SubmitDiceRollResult", new RollDiceResultsList {Results = e});
        }
        catch (Exception ex)
        {
            //ToDo add real logging and user notification component.
            Console.WriteLine(ex);
        }
    }
}