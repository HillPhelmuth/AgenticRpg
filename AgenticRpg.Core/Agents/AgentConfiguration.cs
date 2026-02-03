using AgenticRpg.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgenticRpg.Core.Agents;

public class AgentStaticConfiguration
{
    public static AgentConfiguration Default { get; private set; }

    public static void Configure(IConfiguration configuration)
    {
        Default = new AgentConfiguration
        {
            OpenAIApiKey = configuration["OpenAI:ApiKey"],
            OpenRouterApiKey = configuration["OpenRouter:ApiKey"],
            BlobStorageConnectionString = configuration["AzureStorage:ConnectionString"],
            GoogleApiKey = configuration["GoogleAI:ApiKey"],
            LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole())
        };
    }
}
/// <summary>
/// Configuration settings for AI agents
/// </summary>
public class AgentConfiguration
{
    /// <summary>
    /// Optional Alternative OpenAI endpoint URL
    /// </summary>
    public string? OpenAIEndpoint { get; set; }
    
    /// <summary>
    /// OpenAI API key
    /// </summary>
    public string OpenAIApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Deployment name/model to use
    /// </summary>
    public string BaseModelName { get; set; } = "gpt-5.1";
    public string OpenRouterApiKey { get; set; } = string.Empty;
    public string OpenRouterEndpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string BlobStorageConnectionString { get; set; } = string.Empty;
    public string GoogleApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum conversation history to maintain
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 20;
    public ILoggerFactory LoggerFactory { get; set; }
}

/// <summary>
/// Provides campaign context to agents
/// </summary>
public interface IAgentContextProvider
{
    /// <summary>
    /// Gets the current game state for a campaign
    /// </summary>
    Task<GameState?> GetGameStateAsync(string campaignId);
    
    /// <summary>
    /// Updates the game state for a campaign
    /// </summary>
    Task UpdateGameStateAsync(GameState gameState);

    // Session-specific methods
    
    /// <summary>
    /// Gets the session state for a pre-game session
    /// </summary>
    Task<SessionState?> GetSessionStateAsync(string sessionId);

    /// <summary>
    /// Determines if an ID is a session or campaign
    /// </summary>
    Task<bool> IsSessionAsync(string id);
}

/// <summary>
/// Represents a message in agent conversation
/// </summary>
public class Message
{
    public string Role { get; set; } = string.Empty; // system, user, assistant
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = [];
}
