using System.Net.Http.Json;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Client.Services;

public interface ICharacterService
{
    Task<Character?> GetCharacterByIdAsync(string id);
    Task<IEnumerable<Character>> GetCharactersByCampaignAsync(string campaignId);
    Task<IEnumerable<Character>> GetCharactersByPlayerAsync(string playerId);
    Task<Character> CreateCharacterAsync(Character character);
    Task<Character> UpdateCharacterAsync(Character character);
    Task DeleteCharacterAsync(string id);
}

public class CharacterService : ICharacterService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CharacterService> _logger;
    private const string BaseRoute = "api/characters";

    public CharacterService(HttpClient httpClient, ILogger<CharacterService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Character?> GetCharacterByIdAsync(string id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Character>($"{BaseRoute}/{id}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Character {CharacterId} not found", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching character {CharacterId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Character>> GetCharactersByCampaignAsync(string campaignId)
    {
        try
        {
            var characters = await _httpClient.GetFromJsonAsync<IEnumerable<Character>>($"{BaseRoute}/campaign/{campaignId}");
            return characters ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching characters for campaign {CampaignId}", campaignId);
            return [];
        }
    }

    public async Task<IEnumerable<Character>> GetCharactersByPlayerAsync(string playerId)
    {
        try
        {
            var characters = await _httpClient.GetFromJsonAsync<IEnumerable<Character>>($"{BaseRoute}/player/{playerId}");
            return characters ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching characters for player {PlayerId}", playerId);
            return [];
        }
    }

    public async Task<Character> CreateCharacterAsync(Character character)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BaseRoute, character);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<Character>();
            return created ?? throw new InvalidOperationException("Failed to deserialize created character");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating character");
            throw;
        }
    }

    public async Task<Character> UpdateCharacterAsync(Character character)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseRoute}/{character.Id}", character);
            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<Character>();
            return updated ?? throw new InvalidOperationException("Failed to deserialize updated character");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating character {CharacterId}", character.Id);
            throw;
        }
    }

    public async Task DeleteCharacterAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BaseRoute}/{id}");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting character {CharacterId}", id);
            throw;
        }
    }
}
