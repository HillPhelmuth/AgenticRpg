using System.Net.Http.Json;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.Client.Services;

public interface IWorldService
{
    Task<World?> GetWorldByIdAsync(string worldId);
    Task<IEnumerable<World>> GetAllWorldsAsync();
    Task<IEnumerable<World>> GetTemplateWorldsAsync();
    Task<World> CreateWorldAsync(World world);
    Task<World> UpdateWorldAsync(World world);
    Task<bool> DeleteWorldAsync(string worldId);
}

public class WorldService : IWorldService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorldService> _logger;
    private const string BaseRoute = "api/worlds";

    public WorldService(HttpClient httpClient, ILogger<WorldService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<World?> GetWorldByIdAsync(string worldId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<World>($"{BaseRoute}/{worldId}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("World {WorldId} not found", worldId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting world {WorldId}", worldId);
            return null;
        }
    }

    public async Task<IEnumerable<World>> GetAllWorldsAsync()
    {
        try
        {
            var worlds = await _httpClient.GetFromJsonAsync<IEnumerable<World>>(BaseRoute);
            return worlds ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all worlds");
            return [];
        }
    }

    public async Task<IEnumerable<World>> GetTemplateWorldsAsync()
    {
        try
        {
            var worlds = await _httpClient.GetFromJsonAsync<IEnumerable<World>>($"{BaseRoute}/templates");
            return worlds ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting template worlds");
            return [];
        }
    }

    public async Task<World> CreateWorldAsync(World world)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BaseRoute, world);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<World>();
            return created ?? throw new InvalidOperationException("Failed to deserialize created world");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating world");
            throw;
        }
    }

    public async Task<World> UpdateWorldAsync(World world)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseRoute}/{world.Id}", world);
            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<World>();
            return updated ?? throw new InvalidOperationException("Failed to deserialize updated world");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating world {WorldId}", world.Id);
            throw;
        }
    }

    public async Task<bool> DeleteWorldAsync(string worldId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BaseRoute}/{worldId}");
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting world {WorldId}", worldId);
            return false;
        }
    }
}
