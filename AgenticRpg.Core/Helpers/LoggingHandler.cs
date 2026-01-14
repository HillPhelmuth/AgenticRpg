using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Helpers;

public sealed class LoggingHandler(HttpMessageHandler innerHandler, ILoggerFactory loggerFactory) : DelegatingHandler(innerHandler)
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { WriteIndented = true };

    //private readonly TextWriter _output = output;
    private readonly ILogger<LoggingHandler> _logger = loggerFactory.CreateLogger<LoggingHandler>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {

        _logger.LogInformation(request.RequestUri?.ToString());
        var isStream = false;
        if (request.Content is not null)
        {
            var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            //_logger.LogInformation("=== REQUEST ===");
            try
            {
                var root = JsonNode.Parse(requestBody) as JsonObject;
                if (root is not null)
                {
                    isStream = root["stream"]?.GetValue<bool>() ?? false;

                    var toolsArray = root["tools"]?.AsArray();
                    _logger.LogInformation("Original Tool Count: {ToolCount}", toolsArray?.Count ?? 0);
                    if (toolsArray is not null)
                    {
                        // # Reason: some callers accidentally append duplicate tool definitions; providers may reject or waste tokens.
                        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                        var dedupedTools = new JsonArray();
                       
                        foreach (var toolNode in toolsArray)
                        {
                            if (toolNode is null)
                            {
                                continue;
                            }

                            var key = toolNode["function"]?["name"]?.GetValue<string>()
                                      ?? toolNode["name"]?.GetValue<string>()
                                      ?? toolNode.ToJsonString();

                            if (seenKeys.Add(key))
                            {
                                // JsonNode can only have one parent; clone to move into a new array.
                                dedupedTools.Add(JsonNode.Parse(toolNode.ToJsonString()));
                                
                            }
                        }
                        
                        root["tools"] = dedupedTools;
                        _logger.LogInformation("Tool Count: {ToolCount}", dedupedTools.Count);

                        var updatedBody = root.ToJsonString();
                        if (!string.Equals(updatedBody, requestBody, StringComparison.Ordinal))
                        {
                            request.Content = new StringContent(updatedBody, Encoding.UTF8, "application/json");
                        }
                    }
                }

                var formattedContent = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(requestBody), s_jsonSerializerOptions);
                //_logger.LogInformation("=== REQUEST ===\n{formatted}\n=====", formattedContent);
            }
            catch (JsonException)
            {
                _logger.LogInformation(requestBody);
            }
            _logger.LogInformation(string.Empty);
        }

        // Call the next handler in the pipeline
        var responseMessage = await base.SendAsync(request, cancellationToken);

        if (isStream) return responseMessage;
        var responseBody = await responseMessage.Content.ReadAsStringAsync(cancellationToken);
        //_logger.LogInformation("=== RESPONSE ===");
        //_logger.LogInformation(responseBody);
        //_logger.LogInformation("==============");
        return responseMessage;


    }
}