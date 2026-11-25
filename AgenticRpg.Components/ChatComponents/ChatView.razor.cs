using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

public partial class ChatView : IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = default!;
    [Parameter]
    public List<ChatMessage> Messages { get; set; } = [];

    [Parameter]
    public EventCallback<ChatMessage> OnMessageDeleted { get; set; }

    [Parameter]
    public EventCallback<ChatMessage> OnMessageEdited { get; set; }

    [Parameter]
    public EventCallback<string> OnSuggestedActionInvoked { get; set; }

    [Parameter]
    public string CssClass { get; set; } = string.Empty;
    [Parameter]
    public EventCallback<(string, string)> OnSpeakText { get; set; }
    [Parameter]
    public bool IsPlaybackActive { get; set; }
    [Parameter]
    public EventCallback CancelPlayback { get; set; }

    [Parameter] public EventCallback OnReloadState { get; set; }
    [Parameter] public EventCallback<string> OnRestoreText { get; set; }
    [Parameter] public bool IsProcessing { get; set; }
    [Parameter] public EventCallback<ChatMessage> OnRetry { get; set; }
    [Parameter] public string? SuggestionContext { get; set; }
    [Parameter] public string? CurrentPlayerId { get; set; }
    private readonly HashSet<string> _expiredSuggestions = [];
    private List<string> RelevantSuggestions
    {
        get
        {
            var latest = Messages.Where(m => !m.IsUser && (m.Status == AgenticRpg.Core.Messaging.MessageProcessingStatus.Completed || m.Status is null && m.SuggestedActions is { Count: > 0 } && !m.IsStreaming))
                .OrderByDescending(m => m.Timestamp).FirstOrDefault();
            if (latest is null) return [];
            if (SuggestionContext is not null && latest.SuggestionContext != SuggestionContext) _expiredSuggestions.Add(latest.Id);
            if (_expiredSuggestions.Contains(latest.Id) || Messages.Any(m => m.IsUser && m.IsAuthoritative && !m.WasRejected && m.Timestamp > latest.Timestamp)) return [];
            return latest.SuggestedActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct().ToList() ?? [];
        }
    }
    private ElementReference _messagesContainer;
    private bool _isAtBottom = true;
    private bool _hasUnread;
    [JSInvokable] public async Task NotifyNewContent()
    {
        if (_disposed || _isAtBottom || _hasUnread) return;
        await InvokeAsync(() => { _hasUnread = true; StateHasChanged(); });
    }
    private ChatScrollInterop? _scroll;
    private DotNetObjectReference<ChatView>? _dotNetReference;
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scroll = new ChatScrollInterop(JS);
            await _scroll.InitializeAsync(_messagesContainer, _dotNetReference = DotNetObjectReference.Create(this));
        }

        
    }

    private async Task SpeakMessage(string message, string messageId)
    {
        await OnSpeakText.InvokeAsync((message, messageId));
    }
    [JSInvokable]
    public async Task UpdateScrollPosition(bool isAtBottom)
    {
        if (_disposed) return;
        if (_isAtBottom == isAtBottom && (!isAtBottom || !_hasUnread)) return;
        await InvokeAsync(() => { _isAtBottom = isAtBottom; if (isAtBottom) _hasUnread = false; StateHasChanged(); });
    }

    private async Task ScrollToBottom()
    {
        if (_scroll != null)
        {
            await _scroll.ScrollToBottomAsync();
            _isAtBottom = true;
        }
    }

    private async Task HandleDelete(ChatMessage message)
    {
        if (OnMessageDeleted.HasDelegate)
        {
            await OnMessageDeleted.InvokeAsync(message);
        }
    }

    private async Task HandleEdit(ChatMessage message)
    {
        if (OnMessageEdited.HasDelegate)
        {
            await OnMessageEdited.InvokeAsync(message);
        }
    }

    private async Task HandleSuggestedAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action) || IsProcessing)
        {
            return;
        }

        if (OnSuggestedActionInvoked.HasDelegate)
        {
            await OnSuggestedActionInvoked.InvokeAsync(action.Trim());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_scroll != null)
            {
                await _scroll.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        finally { _dotNetReference?.Dispose(); }
    }
}
