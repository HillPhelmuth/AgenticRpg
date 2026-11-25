using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

internal sealed class ChatScrollInterop(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./_content/AgenticRpg.Components/ChatComponents/ChatView.razor.js";
    private IJSObjectReference? _module;
    private ElementReference _container;
    public async ValueTask InitializeAsync(ElementReference container, DotNetObjectReference<ChatView> callback)
    {
        _container = container;
        _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _module.InvokeVoidAsync("initializeScrollTracking", container, callback);
    }
    public async ValueTask ScrollToBottomAsync()
    {
        if (_module is not null) await _module.InvokeVoidAsync("scrollToBottom", _container);
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is null) return;
            await _module.InvokeVoidAsync("disposeScrollTracking", _container);
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
