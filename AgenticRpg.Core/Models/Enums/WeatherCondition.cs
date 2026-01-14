using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Weather conditions in the game world
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeatherCondition
{
    Clear,
    Rainy,
    Stormy,
    Foggy,
    Snowy,
    Overcast,
    Windy,
    Cloudy
}
