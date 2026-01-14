using AgenticRpg.Core.Messaging;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgenticRpg.Components.ChatComponents;

public partial class MessageView
{
    [Parameter, EditorRequired]
    public ChatMessage Message { get; set; } = null!;

    [Parameter]
    public EventCallback<ChatMessage> OnEdit { get; set; }

    [Parameter]
    public EventCallback<ChatMessage> OnDelete { get; set; }

    private string _editContent = string.Empty;
    private static readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    protected override void OnParametersSet()
    {
        _editContent = Message.Content;
    }

    private string ConvertMarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _markdownPipeline);
    }

    private void StartEdit()
    {
        Message.IsEditing = true;
        _editContent = Message.Content;
    }

    private void CancelEdit()
    {
        Message.IsEditing = false;
        _editContent = Message.Content;
    }

    private async Task SaveEdit()
    {
        Message.Content = _editContent;
        Message.IsEditing = false;

        if (OnEdit.HasDelegate)
        {
            await OnEdit.InvokeAsync(Message);
        }
    }

    private async Task DeleteMessage()
    {
        if (OnDelete.HasDelegate)
        {
            await OnDelete.InvokeAsync(Message);
        }
    }

    private string GetStatusCss(MessageProcessingStatus status) => status switch
    {
        MessageProcessingStatus.Queued => "status-queued",
        MessageProcessingStatus.Processing => "status-processing",
        MessageProcessingStatus.Completed => "status-completed",
        MessageProcessingStatus.Failed => "status-failed",
        _ => string.Empty
    };

    private string GetStatusLabel(MessageProcessingStatus status) => status switch
    {
        MessageProcessingStatus.Queued => "Queued",
        MessageProcessingStatus.Processing => "Processing",
        MessageProcessingStatus.Completed => "Resolved",
        MessageProcessingStatus.Failed => "Failed",
        _ => ""
    };

    private string? GetStatusSupplement()
    {
        if (!Message.Status.HasValue)
        {
            return null;
        }

        return Message.Status.Value switch
        {
            MessageProcessingStatus.Queued when Message.QueuePosition.HasValue =>
                Message.QueuePosition.Value <= 0 ? "Next" : $"{Message.QueuePosition.Value} ahead",
            MessageProcessingStatus.Processing => "Resolving",
            MessageProcessingStatus.Failed when string.IsNullOrEmpty(Message.StatusNote) => "Retry?",
            _ => null
        };
    }

    private string? GetStatusTooltip()
    {
        return string.IsNullOrWhiteSpace(Message.StatusNote) ? null : Message.StatusNote;
    }
}