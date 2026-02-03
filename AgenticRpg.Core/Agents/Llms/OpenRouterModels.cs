using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using AgenticRpg.Core.Helpers;

namespace AgenticRpg.Core.Agents.Llms;

public class OpenRouterModels
{
    [JsonPropertyName("data")]
    public List<OpenRouterModel> Data { get; set; }

    public static List<string> GetAllModelsFromEmbeddedFile()
    {
        var allModels = FileHelper.ExtractFromAssembly<OpenRouterModels>("OpenRouterModels.json").GetModelsWithToolsAndStructuredOutputs();
        return allModels.Select(x => x.Id).OrderBy(x => x).ToList();
    }
    public static List<OpenRouterModel> GetAllModelDataFromEmbeddedFile()
    {
        var allModels = FileHelper.ExtractFromAssembly<OpenRouterModels>("OpenRouterModels.json").GetModelsWithToolsAndStructuredOutputs();
        return allModels;
    }
    public static bool SupportsParameter(string modelName, string parameter)
    {
        var allModels = FileHelper.ExtractFromAssembly<OpenRouterModels>("OpenRouterModels.json");
        var model = allModels.Data.FirstOrDefault(x => x.Id.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        var supportsParameter = model?.SupportsParameter(parameter) ?? false;
        Console.WriteLine($"Model '{modelName}' supports parameter '{parameter}': {supportsParameter}");
        return supportsParameter;
    }
}
public static class OpenRouterModelExtensions
{
    public static bool SupportsParameter(this OpenRouterModel model, string parameter)
    {
        return model.SupportedParameters.Contains(parameter);
    }
    public static List<OpenRouterModel> GetModelsWithToolsAndStructuredOutputs(this OpenRouterModels allModelData, decimal maxPromptPrice = 5.0m, decimal maxCompletionPrice = 16.0m)
    {
        return allModelData.Data
            .Where(model => model.SupportedParameters.Contains("tools") && model.SupportedParameters.Contains("tool_choice") && model.SupportedParameters.Contains("structured_outputs") && model.Architecture.OutputModalities.Contains("text") && model.Pricing.PromptPerMillion() <= maxPromptPrice && model.Pricing.CompletionPerMillion() <= maxCompletionPrice).OrderBy(x => x.Id)
            .ToList();
    }
    public static bool ModelSupportsImageInput(this OpenRouterModel model)
    {
        return model?.Architecture.InputModalities.Contains("image") ?? false;
    }
    
}
public class OpenRouterModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("canonical_slug")]
    public string CanonicalSlug { get; set; }

    [JsonPropertyName("hugging_face_id")]
    public string HuggingFaceId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("context_length")]
    public long ContextLength { get; set; }

    [JsonPropertyName("architecture")]
    public Architecture Architecture { get; set; }

    [JsonPropertyName("pricing")]
    public Pricing Pricing { get; set; }

    [JsonPropertyName("top_provider")]
    public TopProvider TopProvider { get; set; }

    [JsonPropertyName("per_request_limits")]
    public object PerRequestLimits { get; set; }

    [JsonPropertyName("supported_parameters")]
    public List<string> SupportedParameters { get; set; }

    [JsonPropertyName("default_parameters")]
    public DefaultParameters DefaultParameters { get; set; }
}

public class Architecture
{
    [JsonPropertyName("modality")]
    public string Modality { get; set; }

    [JsonPropertyName("input_modalities")]
    public List<string> InputModalities { get; set; }

    [JsonPropertyName("output_modalities")]
    public List<string> OutputModalities { get; set; }

    [JsonPropertyName("tokenizer")]
    public string Tokenizer { get; set; }

    [JsonPropertyName("instruct_type")]
    public string InstructType { get; set; }
}

public class DefaultParameters
{
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    [JsonPropertyName("frequency_penalty")]
    public object FrequencyPenalty { get; set; }
}

public class Pricing
{
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    public decimal? PromptPerMillion()
    {
        if (decimal.TryParse(Prompt, out var promptCost))
        {
            return promptCost * 1_000_000;
        }
        return null;
    }

    [JsonPropertyName("completion")]
    public string Completion { get; set; }
    public decimal? CompletionPerMillion()
    {
        if (decimal.TryParse(Completion, out var completionCost))
        {
            return completionCost * 1_000_000;
        }
        return null;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("request")]
    public string Request { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("web_search")]
    public string WebSearch { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("internal_reasoning")]
    public string InternalReasoning { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("input_cache_read")]
    public string InputCacheRead { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("input_cache_write")]
    public string InputCacheWrite { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("audio")]
    public string Audio { get; set; }
}

public class TopProvider
{
    [JsonPropertyName("context_length")]
    public long? ContextLength { get; set; }

    [JsonPropertyName("max_completion_tokens")]
    public long? MaxCompletionTokens { get; set; }

    [JsonPropertyName("is_moderated")]
    public bool IsModerated { get; set; }
}