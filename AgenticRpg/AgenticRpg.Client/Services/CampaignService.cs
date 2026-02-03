using System.Net.Http.Json;
using AgenticRpg.Core.Models;

namespace AgenticRpg.Client.Services;

public interface ICampaignService
{
    Task<IEnumerable<Campaign>> GetAllCampaignsAsync(int skip = 0, int take = 50);
    Task<Campaign?> GetCampaignByIdAsync(string id);
    Task<IEnumerable<Campaign>> GetCampaignsByOwnerAsync(string ownerId);
    Task<Campaign?> GetCampaignByInvitationCodeAsync(string invitationCode, string userId);
    Task<Campaign> CreateCampaignAsync(Campaign campaign);
    Task<Campaign> UpdateCampaignAsync(Campaign campaign);
    Task DeleteCampaignAsync(string id);
}

public class CampaignService : ICampaignService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CampaignService> _logger;
    private const string BaseRoute = "api/campaigns";

    public CampaignService(HttpClient httpClient, ILogger<CampaignService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<Campaign>> GetAllCampaignsAsync(int skip = 0, int take = 50)
    {
        try
        {
            var campaigns = await _httpClient.GetFromJsonAsync<IEnumerable<Campaign>>($"{BaseRoute}?skip={skip}&take={take}");
            return campaigns ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaigns");
            return [];
        }
    }

    public async Task<Campaign?> GetCampaignByIdAsync(string id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Campaign>($"{BaseRoute}/{id}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Campaign {CampaignId} not found", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaign {CampaignId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Campaign>> GetCampaignsByOwnerAsync(string ownerId)
    {
        try
        {
            var campaigns = await _httpClient.GetFromJsonAsync<IEnumerable<Campaign>>($"{BaseRoute}/owner/{ownerId}");
            return campaigns ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaigns for owner {OwnerId}", ownerId);
            return [];
        }
    }

    /// <summary>
    /// Gets a campaign by invitation code and records the invited user.
    /// </summary>
    /// <param name="invitationCode">The invitation code provided to the player.</param>
    /// <param name="userId">The user ID to record as invited.</param>
    /// <returns>The campaign, or null if not found.</returns>
    public async Task<Campaign?> GetCampaignByInvitationCodeAsync(string invitationCode, string userId)
    {
        if (string.IsNullOrWhiteSpace(invitationCode) || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        try
        {
            return await _httpClient.GetFromJsonAsync<Campaign>($"{BaseRoute}/invitation/{invitationCode}?userId={Uri.EscapeDataString(userId)}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Invitation code {InvitationCode} not found", invitationCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campaign for invitation code {InvitationCode}", invitationCode);
            return null;
        }
    }

    public async Task<Campaign> CreateCampaignAsync(Campaign campaign)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BaseRoute, campaign);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<Campaign>();
            return created ?? throw new InvalidOperationException("Failed to deserialize created campaign");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating campaign");
            throw;
        }
    }

    public async Task<Campaign> UpdateCampaignAsync(Campaign campaign)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseRoute}/{campaign.Id}", campaign);
            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<Campaign>();
            return updated ?? throw new InvalidOperationException("Failed to deserialize updated campaign");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating campaign {CampaignId}", campaign.Id);
            throw;
        }
    }

    public async Task DeleteCampaignAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BaseRoute}/{id}");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting campaign {CampaignId}", id);
            throw;
        }
    }
}
