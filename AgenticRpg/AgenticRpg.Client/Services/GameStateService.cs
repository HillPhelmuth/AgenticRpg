using System.Net.Http.Json;
using AgenticRpg.Core.Models;

namespace AgenticRpg.Client.Services;

/// <summary>
/// Service for managing game state from the client
/// </summary>
public class GameStateService : IGameStateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GameStateService> _logger;

    public GameStateService(HttpClient httpClient, ILogger<GameStateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<GameState?> GetGameStateAsync(string campaignId)
    {
        try
        {
            _logger.LogInformation("Fetching game state for campaign: {CampaignId}", campaignId);
            
            var response = await _httpClient.GetAsync($"api/gamestate/{campaignId}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch game state: {StatusCode}", response.StatusCode);
                return null;
            }
            
            var gameState = await response.Content.ReadFromJsonAsync<GameState>();
            _logger.LogInformation("Successfully fetched game state for campaign: {CampaignId}", campaignId);
            
            return gameState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching game state for campaign: {CampaignId}", campaignId);
            return null;
        }
    }
}
