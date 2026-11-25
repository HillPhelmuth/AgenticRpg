using System.Net.Http.Json;
using AgenticRpg.Core.Models;
using AgenticRpg.Client.Services;
using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Core.Hubs;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Client.Layout;

public abstract class RpgPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] private HttpClient RecoveryHttp { get; set; } = default!;
    protected string? TranscriptCursor { get; private set; }
    protected string? RecoveryScope { get; private set; }
    protected bool ReviewRequired { get; private set; }
    protected string? RecoveryError { get; private set; }
    protected void ObserveRuntime(RuntimeMessage message, string playerId)
    {
        RecoveryScope ??= message.ScopeId;
        if (message.PlayerId == playerId && message.NeedsReview)
            ReviewRequired = true;
    }
    protected async Task CancelPending(RuntimeChatState chat, string scopeId, string playerId)
    {
        if (chat.Pending(playerId) is not { } pending) return;
        try { await HubService.CancelRequestAsync(scopeId, pending.ClientMessageId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Cancel failed"); RecoveryError = "Stopping wasn't confirmed. Reconnect before trying again, impatient mortal."; }
    }
    protected void RememberRecovery(ScopeRecoverySnapshot snapshot)
    {
        RecoveryScope = snapshot.ScopeId;
        TranscriptCursor = snapshot.TranscriptCursor;
        ReviewRequired = snapshot.RequiresReview;
    }
    protected async Task ConfirmReview()
    {
        try
        {
            using var response = await RecoveryHttp.PostAsync($"/api/scopes/{Uri.EscapeDataString(RecoveryScope!)}/review", null);
            if (response.IsSuccessStatusCode) ReviewRequired = false;
            else RecoveryError = "Review could not be confirmed. Reload the current state.";
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Review acknowledgement failed"); RecoveryError = "Reconnect to confirm your review."; }
    }
    protected async Task LoadEarlier(List<ChatMessage> chat, RuntimeChatState reducer, string playerId, string? characterName = null)
    {
        try
        {
            var page = await RecoveryHttp.GetFromJsonAsync<TranscriptPage>($"/api/scopes/{Uri.EscapeDataString(RecoveryScope!)}/transcript?cursor={Uri.EscapeDataString(TranscriptCursor!)}");
            if (page is null) return;
            foreach (var m in page.Messages) reducer.Apply(chat, m, playerId, characterName);
            chat.Sort((a, b) => a.Timestamp != b.Timestamp ? a.Timestamp.CompareTo(b.Timestamp) : a.IsUser != b.IsUser ? (a.IsUser ? -1 : 1) : string.CompareOrdinal(a.Id, b.Id));
            TranscriptCursor = page.Cursor;
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Transcript load failed"); RecoveryError = "Earlier messages could not be loaded. Try again."; }
    }
    private readonly List<IDisposable> _subscriptions = [];
    protected bool IsDisposed { get; private set; }
    protected string ScopeLeaseId { get; } = Guid.NewGuid().ToString("N");
    protected string ConnectionStatusLabel => HubService.ConnectionStatus.ToString();
    protected bool HubConnected => HubService.ConnectionStatus == GameConnectionStatus.Connected;
    protected string? ConnectionError { get; private set; }
    protected void Track(IDisposable subscription) => _subscriptions.Add(subscription);
    protected async Task RetryConnection()
    {
        try { ConnectionError = null; await HubService.RetryConnectionAsync(); }
        catch (Exception ex) { Logger.LogWarning(ex, "Reconnect failed"); ConnectionError = "Connection failed. Try reconnecting."; }
    }
    private async void HandleConnectionStatus(GameConnectionStatus status)
    {
        if (IsDisposed) return;
        try { await InvokeAsync(() => { if (!IsDisposed) StateHasChanged(); }); }
        catch (Exception ex) { Logger.LogWarning(ex, "Connection status refresh failed"); }
    }
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
        Track(HubService.OnConnectionStatusChanged(HandleConnectionStatus));
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

    protected void DisposeSubscriptions()
    {
        IsDisposed = true;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        HubService.OnDiceRollResult -= HandleDiceRollResult;
    }

    public virtual async ValueTask DisposeAsync()
    {
        DisposeSubscriptions();
        _speechCts?.Cancel();
        _speechCts?.Dispose();
        await AudioInterop.StopAsync();
    }
}
