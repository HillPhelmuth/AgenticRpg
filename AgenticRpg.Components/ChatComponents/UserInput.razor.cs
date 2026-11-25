using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

public partial class UserInput : IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Parameter] public string? AccountId { get; set; }
    [Parameter] public string? ScopeId { get; set; }
    [Parameter] public Func<bool>? SubmissionAccepted { get; set; }
    private IJSObjectReference? _draftModule;
    private string? _draftKey;
    private string? _submissionError;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrEmpty(AccountId) || string.IsNullOrEmpty(ScopeId)) return;
        var key = $"rpg-draft:{Uri.EscapeDataString(AccountId)}:{ScopeId}";
        if (_draftKey == key) return;
        try
        {
            _draftModule ??= await Js.InvokeAsync<IJSObjectReference>("import", "./_content/AgenticRpg.Components/ChatComponents/draftInput.js");
            _draftKey = key;
            _inputValue = await _draftModule.InvokeAsync<string>("get", key);
            StateHasChanged();
        }
        catch (JSException) { }
    }
    private async Task PersistDraft()
    {
        if (_draftModule is not null && _draftKey is not null)
            try { await _draftModule.InvokeVoidAsync("set", _draftKey, _inputValue); } catch (JSException) { }
    }
    public async ValueTask DisposeAsync()
    { if (_draftModule is not null) { try { await _draftModule.DisposeAsync(); } catch (JSDisconnectedException) { } } }
    [Parameter]
    public EventCallback<string> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public bool IsProcessing { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = "Type your message...";

    [Parameter]
    public string HintText { get; set; } = "";

    [Parameter]
    public int Rows { get; set; } = 3;

    [Parameter]
    public string CssClass { get; set; } = string.Empty;

    [Parameter] public bool IsUnavailable { get; set; }
    [Parameter] public bool DraftDisabled { get; set; }
    private string _inputValue = string.Empty;
    private bool _isTextArea;
    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        // Submit on Enter (without Shift)
        if (e.Key == "Enter" && !e.ShiftKey && !IsUnavailable)
        {
            await SubmitMessage();
        }
    }

    private async Task SubmitMessage()
    {
        if (string.IsNullOrWhiteSpace(_inputValue) || IsProcessing || IsUnavailable)
            return;

        var message = _inputValue.Trim();
        await PersistDraft();
        _submissionError = null;

        if (OnSubmit.HasDelegate)
        {
            try
            {
                await OnSubmit.InvokeAsync(message);
                if (SubmissionAccepted?.Invoke() != false)
                { if (_inputValue.Trim() == message) _inputValue = ""; await PersistDraft(); }
                else _submissionError = "Delivery not confirmed. Reconnect, then send again with the same request ID.";
            }
            catch { _submissionError = "Delivery not confirmed. Your text is retained; reconnect and try again."; }
        }
    }
    public async Task SubmitMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsProcessing || IsUnavailable)
            return;
        if (OnSubmit.HasDelegate)
        {
            await OnSubmit.InvokeAsync(message);
            
        }
    } 
    private async Task CancelProcessing()
    {
        Console.WriteLine("CancelProcessing called");
        if (OnCancel.HasDelegate)
        {
            await OnCancel.InvokeAsync();
            Console.WriteLine("OnCancel invoked");
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Clears the input field. Can be called from parent component.
    /// </summary>
    public void Clear()
    {
        _inputValue = string.Empty;
        StateHasChanged();
    }

    /// <summary>
    /// Sets the input value. Can be called from parent component.
    /// </summary>
    public void SetValue(string value)
    {
        _inputValue = value;
        _ = PersistDraft();
        StateHasChanged();
    }
}
