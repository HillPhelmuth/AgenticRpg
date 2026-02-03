using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Client.Pages;
public partial class RpgDropdown
{
    [Parameter]
    public Campaign? SelectedCampaign { get; set; }
    [Parameter]
    public string? CurrentUserId { get; set; }
    [Inject] private ILogger<RpgDropdown> Logger { get; set; } = null!;
    private IReadOnlyList<string> AvailableModels { get; set; } = OpenRouterModels.GetAllModelsFromEmbeddedFile();
    private string? _selectedModel;
    private string _modelFilter = string.Empty;
    private bool _isDropdownOpen;
    private string? ModelSelectionMessage { get; set; }
    private string? ModelSelectionError { get; set; }
    private CancellationTokenSource? _messageCts;
    private IEnumerable<string> FilteredModels => string.IsNullOrWhiteSpace(_modelFilter)
        ? AvailableModels
        : AvailableModels.Where(model => model.Contains(_modelFilter, StringComparison.OrdinalIgnoreCase));
    private bool CanApplyModelOverride => !string.IsNullOrEmpty(_selectedModel);
    private void ToggleDropdown()
    {
        _isDropdownOpen = !_isDropdownOpen;
        if (_isDropdownOpen)
        {
            _modelFilter = string.Empty;
        }
    }

    private void SelectModelOption(string model)
    {
        _selectedModel = model;
        _modelFilter = string.Empty;
        _isDropdownOpen = false;
    }

    private async Task SelectModel()
    {
        ModelSelectionMessage = null;
        ModelSelectionError = null;

        if (string.IsNullOrEmpty(_selectedModel))
        {
            ModelSelectionError = "Select a model before applying.";
            await ShowTransientMessageAsync();
            return;
        }

        var campaignId = SelectedCampaign?.Id;
        var campaignName = SelectedCampaign?.Name;
        var scopeDescription = SelectedCampaign != null
            ? $"campaign {campaignName}"
            : "all standalone sessions";

        try
        {
            await HubService.StartAsync();
            await HubService.ChangeModelAsync(campaignId, _selectedModel);
            ModelSelectionMessage = SelectedCampaign != null
                ? $"Applied {_selectedModel} to {campaignName}."
                : $"Applied {_selectedModel} as default for future sessions.";
            Logger.LogInformation("Player {PlayerId} set model {Model} for {Scope}", CurrentUserId, _selectedModel, scopeDescription);
            await ShowTransientMessageAsync();
        }
        catch (Exception ex)
        {
            ModelSelectionError = "Failed to apply model. Please try again.";
            Logger.LogError(ex, "Failed to set model override {Model} for {Scope}", _selectedModel, scopeDescription);
            await ShowTransientMessageAsync();
        }
    }

    private async Task ShowTransientMessageAsync()
    {
        _messageCts?.Cancel();
        _messageCts?.Dispose();
        _messageCts = new CancellationTokenSource();

        // Reason: ensure the UI shows the latest message before auto-clearing it.
        await InvokeAsync(StateHasChanged);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _messageCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        ModelSelectionMessage = null;
        ModelSelectionError = null;
        await InvokeAsync(StateHasChanged);
    }
}
