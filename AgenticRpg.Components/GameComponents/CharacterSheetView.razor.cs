using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Components.GameComponents;

public partial class CharacterSheetView
{
    [Parameter, EditorRequired] public Character? Character { get; set; }

    [Parameter] public string CssClass { get; set; } = string.Empty;

    [Parameter] public int InitialPage { get; set; } = 1;

    private int CurrentPage { get; set; } = 1;

    protected override void OnInitialized()
    {
        CurrentPage = InitialPage;
    }

    protected override void OnParametersSet()
    {
        // Reset to initial page if character changes
        if (Character != null)
        {
            CurrentPage = InitialPage;
        }
    }

    private void SetPage(int page)
    {
        if (page >= 1 && page <= 3)
        {
            CurrentPage = page;
        }
    }

    /// <summary>
    /// Navigates to a specific page. Can be called from parent component.
    /// </summary>
    public void NavigateToPage(int page)
    {
        SetPage(page);
        StateHasChanged();
    }
}