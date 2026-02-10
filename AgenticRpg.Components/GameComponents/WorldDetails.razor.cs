using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Components.GameComponents;

public partial class WorldDetails
{
    /// <summary>
    /// The world data to display.
    /// </summary>
    [Parameter] public World? World { get; set; }

    /// <summary>
    /// Additional CSS classes to apply to the root element.
    /// </summary>
    [Parameter] public string CssClass { get; set; } = string.Empty;

    /// <summary>
    /// The initial page to display when world data is set.
    /// </summary>
    [Parameter] public int InitialPage { get; set; } = 1;

    private int CurrentPage { get; set; } = 1;

    private Location? SelectedLocation { get; set; }

    protected override void OnInitialized()
    {
        // Initialize the current page from the provided parameter.
        CurrentPage = InitialPage;
    }

    protected override void OnParametersSet()
    {
        // Reset to the initial page when the world context changes.
        if (World != null)
        {
            CurrentPage = InitialPage;
        }
    }

    private void SelectLocation(Location location)
    {
        // Track the selected location for context details.
        SelectedLocation = location;
        StateHasChanged();
    }

    private void SetPage(int page)
    {
        // Keep navigation within the defined page range.
        if (page >= 1 && page <= 4)
        {
            CurrentPage = page;
        }
    }

    /// <summary>
    /// Navigates to a specific world details page.
    /// </summary>
    public void NavigateToPage(int page)
    {
        // Allow parent components to change the current page.
        SetPage(page);
        StateHasChanged();
    }
}