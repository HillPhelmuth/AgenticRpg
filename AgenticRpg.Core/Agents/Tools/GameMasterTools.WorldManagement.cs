using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using AgenticRpg.Core.Agents.Tools.Results;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Core.Agents.Tools;

public partial class GameMasterTools
{
    [Description("Updates world state or location information. Parameters: locationId (optional, ID of location to update), newLocationDescription (optional, updated description), weatherCondition (optional: Clear, Rainy, Stormy, Foggy, etc.), timeOfDay (optional: Dawn, Morning, Afternoon, Dusk, Night), campaignId. Returns confirmation of updates.")]
    public async Task<string> UpdateWorldState(
        [Description("The unique Id of the campaign to update. NOT the campaign name!")] string campaignId,
        [Description(
            "Optional. The unique ID of the location to update or set as the current location. If provided, this becomes the party's current location.")]
        string? locationId = null,
        [Description(
            "Optional. A new or updated description for the specified location. Used to reflect changes in the location's state.")]
        string? newLocationDescription = null,
        [Description(
            "Optional. The current weather condition in the area. Values: Clear, Rainy, Stormy, Foggy, Snowy, Overcast.")]
        WeatherCondition? weatherCondition = WeatherCondition.Clear,
        [Description(
            "Optional. The current time of day in the game world. Values: Dawn, Morning, Afternoon, Dusk, Evening, Night, Midnight.")]
        TimeOfDay? timeOfDay = null)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new UpdateWorldStateResult
            {
                Success = false,
                Updates = new List<string>(),
                Message = string.Empty,
                Error = "Campaign not found"
            });
        }

        var updates = new List<string>();

        if (!string.IsNullOrEmpty(locationId) && gameState.World != null)
        {
            var location = gameState.World.Locations.FirstOrDefault(l => l.Id == locationId);
            if (location != null && !string.IsNullOrEmpty(newLocationDescription))
            {
                location.Description = newLocationDescription;
                updates.Add($"Updated location '{location.Name}' description");
            }

            gameState.CurrentLocationId = locationId;
            updates.Add($"Changed current location to '{location?.Name ?? locationId}'");
        }

        if (weatherCondition.HasValue)
        {
            gameState.Metadata["Weather"] = weatherCondition.Value.ToString();
            updates.Add($"Weather set to: {weatherCondition.Value}");
        }

        if (timeOfDay.HasValue)
        {
            gameState.Metadata["TimeOfDay"] = timeOfDay.Value.ToString();
            updates.Add($"Time of day set to: {timeOfDay.Value}");
        }

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new UpdateWorldStateResult
        {
            Success = true,
            Updates = updates,
            Message = "World state updated successfully"
        });
    }

    [Description("Updates high-level world details such as name, description, theme, geography, or political climate and persists the campaign state.")]
    public async Task<string> UpdateWorldDetails(
        [Description("The unique Id of the campaign whose world should be updated. NOT the campaign name!")] string campaignId,
        [Description("Optional new world name.")] string? name = null,
        [Description("Optional updated world description.")] string? description = null,
        [Description("Optional updated theme or genre.")] string? theme = null,
        [Description("Optional updated geography details.")] string? geography = null,
        [Description("Optional updated politics/faction details.")] string? politics = null,
        [Description("Optional flag to mark the world as a reusable template.")] bool? isTemplate = null)
    {
        var gameState = await stateManager.GetCampaignStateAsync(campaignId);
        if (gameState == null)
        {
            return JsonSerializer.Serialize(new WorldUpdateResult
            {
                Success = false,
                Message = "Campaign not found",
                Error = "Campaign not found"
            });
        }

        gameState.World ??= new World();
        var world = gameState.World;
        var updates = new List<string>();

        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(world.Name, name, StringComparison.Ordinal))
        {
            world.Name = name;
            updates.Add("Name");
        }

        if (!string.IsNullOrWhiteSpace(description) && !string.Equals(world.Description, description, StringComparison.Ordinal))
        {
            world.Description = description;
            updates.Add("Description");
        }

        if (!string.IsNullOrWhiteSpace(theme) && !string.Equals(world.Theme, theme, StringComparison.Ordinal))
        {
            world.Theme = theme;
            updates.Add("Theme");
        }

        if (!string.IsNullOrWhiteSpace(geography) && !string.Equals(world.Geography, geography, StringComparison.Ordinal))
        {
            world.Geography = geography;
            updates.Add("Geography");
        }

        if (!string.IsNullOrWhiteSpace(politics) && !string.Equals(world.Politics, politics, StringComparison.Ordinal))
        {
            world.Politics = politics;
            updates.Add("Politics");
        }

        if (isTemplate.HasValue)
        {
            world.IsTemplate = isTemplate.Value;
            updates.Add("IsTemplate");
        }

        if (updates.Count == 0)
        {
            return JsonSerializer.Serialize(new WorldUpdateResult
            {
                Success = false,
                Message = "No changes were applied",
                Error = "No updates provided"
            });
        }

        await stateManager.UpdateCampaignStateAsync(gameState);

        return JsonSerializer.Serialize(new WorldUpdateResult
        {
            Success = true,
            World = BuildWorldSnapshot(gameState, true, true, true, true),
            Message = $"Updated world fields: {string.Join(", ", updates)}"
        });
    }
}
