using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

public partial class ChatView
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

    private ElementReference _messagesContainer;
    private bool _isAtBottom = true;
    private IJSObjectReference? _scrollModule;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scrollModule = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/AgenticRpg.Components/ChatComponents/chatView.js");

            await _scrollModule.InvokeVoidAsync(
                "initializeScrollTracking", _messagesContainer,
                DotNetObjectReference.Create(this));
        }

        // Auto-scroll to bottom when new messages arrive
        if (_isAtBottom && Messages.Any())
        {
            await ScrollToBottom();
        }
    }

    [JSInvokable]
    public void UpdateScrollPosition(bool isAtBottom)
    {
        _isAtBottom = isAtBottom;
        StateHasChanged();
    }

    private async Task ScrollToBottom()
    {
        if (_scrollModule != null)
        {
            await _scrollModule.InvokeVoidAsync("scrollToBottom", _messagesContainer);
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
        if (string.IsNullOrWhiteSpace(action))
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
        if (_scrollModule != null)
        {
            await _scrollModule.DisposeAsync();
        }
    }
}