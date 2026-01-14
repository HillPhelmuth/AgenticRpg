using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Components.GameComponents;

public partial class WorldDetails
{
    [Parameter] public World? World { get; set; }

    [Parameter] public string CssClass { get; set; } = string.Empty;

    private Location? SelectedLocation { get; set; }

    private void SelectLocation(Location location)
    {
        SelectedLocation = location;
        StateHasChanged();
    }
}