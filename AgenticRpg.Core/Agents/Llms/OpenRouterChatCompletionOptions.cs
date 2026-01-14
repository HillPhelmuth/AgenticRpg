using System.Text.Json.Serialization;

namespace AgenticRpg.Core.Agents.Llms;

public class OpenRouterChatCompletionOptions : OpenAI.Chat.ChatCompletionOptions
{
    [JsonPropertyName("provider"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Provider? Provider { get; set; }
    [JsonPropertyName("modalities"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Modalities { get; set; }
}