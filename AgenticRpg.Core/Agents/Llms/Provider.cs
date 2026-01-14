using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Agents.Llms;

public class Provider
{
    [JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Only { get; set; }
    [JsonPropertyName("sort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sort { get; set; }
}