using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Hubs;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Client.Layout;

public abstract class RpgPageBase : ComponentBase
{
    protected UserInput UserInputRef { get; set; } = default!;
    [Inject]
    protected IGameHubService HubService { get; set; } = default!;
    [Inject] protected ICampaignService CampaignService { get; set; } = default!;
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = default!;
    private ILogger<RpgPageBase> Logger => LoggerFactory.CreateLogger<RpgPageBase>();
    [Inject] protected AudioInterop AudioInterop { get; set; } = default!;
    protected CancellationTokenSource? _speechCts;
    protected bool _speechGenerating;
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
    /// <summary>
    /// Streams synthesized speech for the provided text and plays it through JS interop.
    /// </summary>
    /// <param name="text">The text to synthesize and play.</param>
    protected async Task PlaySpeechAsync((string, string) valueTuple)
    {
        if (string.IsNullOrWhiteSpace(valueTuple.Item1))
        {
            return;
        }

        try
        {
            _speechGenerating = true;
            _speechCts?.Cancel();
            _speechCts?.Dispose();
            _speechCts = new CancellationTokenSource();
            Console.WriteLine("Starting speech synthesis...");
            await AudioInterop.StopAsync();
            await HubService.StreamSpeechAsync(valueTuple.Item1, valueTuple.Item2, chunk => AudioInterop.PlayPcmAsync(chunk).AsTask(),
                _speechCts.Token);
        }
        catch (OperationCanceledException)
        {
            // No action needed; user-triggered cancellation is expected.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to stream speech audio");
        }
        finally
        {
            _speechGenerating = false;
            StateHasChanged();
        }
    }
    protected async Task CancelPlayback()
    {
        _speechCts?.Cancel();
        await AudioInterop.StopAsync();

    }

}