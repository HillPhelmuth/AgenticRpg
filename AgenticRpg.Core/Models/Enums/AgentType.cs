using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Models.Enums;

/// <summary>
/// Types of specialized agents in the system
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentType
{
    None,
    [Description("Game Master")]
    GameMaster,
    [Description("Character Creation")]
    CharacterCreation,
    [Description("Combat Agent")]
    Combat,
    [Description("Shopkeeper Agent")]
    ShopKeeper,
    [Description("World Builder Agent")]
    WorldBuilder,
    [Description("Character Manager Agent")]
    CharacterManager
}
