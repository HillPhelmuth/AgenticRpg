using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Components.GameComponents;

public partial class WorldDetails
{
    private const string LocationCardKind = "location";
    private const string NpcCardKind = "npc";
    private const string QuestCardKind = "quest";
    private const string LoreCardKind = "lore";

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
    private HashSet<string> ExpandedCards { get; } = [];

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
            ExpandedCards.Clear();
        }
    }

    private bool IsExpanded(string cardKind, string id)
    {
        return ExpandedCards.Contains(GetCardKey(cardKind, id));
    }

    private void ToggleExpanded(string cardKind, string id)
    {
        var cardKey = GetCardKey(cardKind, id);

        if (!ExpandedCards.Add(cardKey))
        {
            ExpandedCards.Remove(cardKey);
        }
    }

    private static string GetToggleText(bool isExpanded)
    {
        return isExpanded ? "Show Less" : "Show More";
    }

    private static string GetCardKey(string cardKind, string id)
    {
        return $"{cardKind}:{id}";
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