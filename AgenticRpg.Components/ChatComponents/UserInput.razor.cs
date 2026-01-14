using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

public partial class UserInput
{
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

    private string _inputValue = string.Empty;
    private bool _isTextArea;
    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        // Submit on Enter (without Shift)
        if (e.Key == "Enter" && !e.ShiftKey && !IsProcessing)
        {
            await SubmitMessage();
        }
    }

    private async Task SubmitMessage()
    {
        if (string.IsNullOrWhiteSpace(_inputValue) || IsProcessing)
            return;

        var message = _inputValue.Trim();
        _inputValue = string.Empty;

        if (OnSubmit.HasDelegate)
        {
            await OnSubmit.InvokeAsync(message);
        }
    }
    public async Task SubmitMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsProcessing)
            return;
        if (OnSubmit.HasDelegate)
        {
            await OnSubmit.InvokeAsync(message);
        }
    } 
    private async Task CancelProcessing()
    {
        if (OnCancel.HasDelegate)
        {
            await OnCancel.InvokeAsync();
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
        StateHasChanged();
    }
}