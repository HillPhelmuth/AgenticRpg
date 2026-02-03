using System.Security.Claims;
using System.Text.Json;
using AgenticRpg.Client.Services;
using AgenticRpg.Components;
using AgenticRpg.Core;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;
using Location = AgenticRpg.Core.Models.Game.Location;
using Results = Microsoft.AspNetCore.Http.Results;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var services = builder.Services;
services.AddRazorComponents()
   .AddInteractiveWebAssemblyComponents().AddInteractiveServerComponents();
services.AddOpenApi();

// Configure authentication and authorization for API endpoints.
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
services.AddCors(options =>
{
    options.AddPolicy("ClientCors", policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


// Add SignalR
services.AddSignalR();

// Configure Cosmos DB Client
var cosmosClient = new CosmosClient(builder.Configuration["ConnectionStrings:CosmosDBConnection"], new CosmosClientOptions()
{
    UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
    {
        // Example: Ignore null values during serialization
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Example: Use camelCase for property names in the serialized JSON
        IgnoreReadOnlyProperties = true,
        PropertyNameCaseInsensitive = true
    }
});
services.AddSingleton(cosmosClient);
services.AddHttpClient();
// Register Agent Configuration and Context Provider
var config = new AgentConfiguration() { OpenAIApiKey = builder.Configuration["OpenAI:ApiKey"]!, OpenRouterApiKey = builder.Configuration["OpenRouter:ApiKey"]! };
AgentStaticConfiguration.Configure(builder.Configuration);
services.AddSingleton(config);
services.AddSingleton<IAgentContextProvider, AgentContextProvider>();

services.AddCoreRpgServices();

services.AddScoped<ICampaignService, CampaignService>();
services.AddScoped<ICharacterService, CharacterService>();
services.AddScoped<IWorldService, WorldService>();
services.AddScoped<IGameStateService, GameStateService>();

// Register Dice Roll Request Service
services.AddRollDiceHubService();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseCors("ClientCors");
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode().AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AgenticRpg.Client._Imports).Assembly);
app.MapHub<GameHub>("/hubs/game");
var dice = app.MapGroup("api/dice").WithTags("Dice");
dice.MapPost("/", async (IRollDiceService diceRollerService, [FromBody] DiceRollRequest request) =>
{
    Console.WriteLine($"Dice Roll Request from API:\n\n {JsonSerializer.Serialize(request)}");
    var diceParameters = request.Parameters ?? new RollDiceParameters();
    diceParameters.Set("DieType", DieType.D20);
    diceParameters.Set("NumberOfRolls", request.NumberOfRollWindows);
    diceParameters.Set("NumberOfDice", request.NumberOfDice);
    diceParameters.Set("IsManual", true);
    var result = await diceRollerService.RequestDiceRoll(request.SessionId, diceParameters, numberOfRollWindows: request.NumberOfRollWindows);
    return Results.Ok(result);
});
#region Campaign Endpoints

// ============================================================================
// CAMPAIGN ENDPOINTS
// ============================================================================

var campaigns = app.MapGroup("/api/campaigns")
    .WithTags("Campaigns")
    .RequireAuthorization();

// Resolves the current user ID from authenticated claims.
static string? GetCurrentUserId(ClaimsPrincipal user)
{
    // Reason: Ensure consistent ownership checks across endpoints.
    return user.FindFirstValue("name")
           ?? user.FindFirstValue(ClaimTypes.Name)
           ?? user.Identity?.Name
           ?? user.FindFirstValue("preferred_username")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue("oid");
}

// GET /api/campaigns - List all campaigns with pagination
campaigns.MapGet("/", async (
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    var userId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var result = await repo.GetByOwnerIdAsync(userId);
    return Results.Ok(result);
})
    .WithName("GetAllCampaigns")
    .Produces<IEnumerable<Campaign>>();

// GET /api/campaigns/{id} - Get campaign by ID
campaigns.MapGet("/{id}", async (
        string id,
        ClaimsPrincipal user,
        ICampaignRepository repo,
        ICharacterRepository characterRepository) =>
{
    var userId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var campaign = await repo.GetByIdAsync(id);
    if (campaign is null)
    {
        return Results.NotFound();
    }

    if (campaign.OwnerId == userId)
    {
        return Results.Ok(campaign);
    }

    // Reason: Allow participants to access campaigns after joining with a character.
    var playerCharacters = await characterRepository.GetByPlayerIdAsync(userId);
    var isParticipant = playerCharacters.Any(character => character.CampaignId == campaign.Id);
    return isParticipant ? Results.Ok(campaign) : Results.Forbid();
})
    .WithName("GetCampaignById")
    .Produces<Campaign>()
    .Produces(StatusCodes.Status404NotFound);

// GET /api/campaigns/owner/{ownerId} - Get campaigns by owner ID
campaigns.MapGet("/owner/{ownerId}", async (
        string ownerId,
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    
    var userId = GetCurrentUserId(user);
    Console.WriteLine($"User Id: {userId}");
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(ownerId, userId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    var result = await repo.GetByOwnerIdAsync(userId);
    return Results.Ok(result);
})
    .WithName("GetCampaignsByOwner")
    .Produces<IEnumerable<Campaign>>();

// GET /api/campaigns/invitation/{invitationCode} - Get campaign by invitation code
campaigns.MapGet("/invitation/{invitationCode}", async (
        string invitationCode,
        [FromQuery] string userId,
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    var currentUserId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(currentUserId))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(userId, currentUserId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(invitationCode))
    {
        return Results.BadRequest("Invitation code is required.");
    }

    var campaign = await repo.GetByInvitationCodeAsync(invitationCode, currentUserId);
    return campaign is not null ? Results.Ok(campaign) : Results.NotFound();
})
    .WithName("GetCampaignByInvitationCode")
    .Produces<Campaign>()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status400BadRequest);

// POST /api/campaigns - Create new campaign
campaigns.MapPost("/", async (
        [FromBody] Campaign campaign,
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    var userId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    // Reason: Enforce ownership server-side regardless of client input.
    campaign.OwnerId = userId;
    var created = await repo.CreateAsync(campaign);
    return Results.Created($"/api/campaigns/{created.Id}", created);
})
    .WithName("CreateCampaign")
    .Produces<Campaign>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// PUT /api/campaigns/{id} - Update existing campaign
campaigns.MapPut("/{id}", async (
        string id,
        [FromBody] Campaign campaign,
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    var userId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    if (id != campaign.Id)
    {
        return Results.BadRequest("ID mismatch");
    }

    var existing = await repo.GetByIdAsync(id);
    if (existing is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(existing.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    campaign.OwnerId = existing.OwnerId;

    var updated = await repo.UpdateAsync(campaign);
    return Results.Ok(updated);
}).WithName("UpdateCampaign")
    .Produces<Campaign>()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status400BadRequest);

// DELETE /api/campaigns/{id} - Delete campaign
campaigns.MapDelete("/{id}", async (
        string id,
        ClaimsPrincipal user,
        ICampaignRepository repo) =>
{
    var userId = GetCurrentUserId(user);
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var existing = await repo.GetByIdAsync(id);
    if (existing is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(existing.OwnerId, userId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    var success = await repo.DeleteAsync(id);
    return success ? Results.NoContent() : Results.NotFound();
}).WithName("DeleteCampaign")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);


#endregion

#region Character Endpoints
// ============================================================================
// CHARACTER ENDPOINTS
// ============================================================================

var characters = app.MapGroup("/api/characters")
    .WithTags("Characters");

// GET /api/characters/{id} - Get character by ID
characters.MapGet("/{id}", async (
        string id,
        ICharacterRepository repo) =>
{
    var character = await repo.GetByIdAsync(id);
    return character is not null ? Results.Ok(character) : Results.NotFound();
})
    .WithName("GetCharacterById")
    .Produces<Character>()
    .Produces(StatusCodes.Status404NotFound);
characters.MapGet("/createVideo/{id}", async (string id, ICharacterRepository repo) =>
{
    // Logic to create a video for the character with the given ID
    var character = await repo.GetByIdAsync(id);
    if (character is null)
    {
        return Results.NotFound();
    }
    var promptTypes = Enum.GetValues<IntroPromptType>().ToList();
    var random = new Random();
    var selectedPromptType = promptTypes[random.Next(promptTypes.Count)];
    var videoUrl = await VideoGenService.GenerateCharacterIntroVideo(character, selectedPromptType);
    character.IntroVidUrl = videoUrl;
    await repo.UpdateAsync(character);

    return Results.Ok(character);
}).WithName("GenerateCharacterVideo").Produces<Character>();
// GET /api/characters/campaign/{campaignId} - Get all characters in an active campaign
characters.MapGet("/campaign/{campaignId}", async (
        string campaignId, ICharacterRepository charRepo, ICampaignRepository campaignRepo) =>
{
    var campaign = await campaignRepo.GetByIdAsync(campaignId);
    if (campaign is null)
    {
        return Results.NotFound();
    }

    var ids = campaign.CharacterIds;
    var playerLobbyIds = campaign.PlayerReadyStatuses.Select(x => x.Value.CharacterId);
    List<Character> results = [];
    foreach (var id in ids)
    {
        var character = await charRepo.GetByIdAsync(id);
        if (character is not null && playerLobbyIds.Contains(character.Id))
        {
            results.Add(character);
        }
    }
    return Results.Ok(results);
})
    .WithName("GetCharactersByCampaign")
    .Produces<IEnumerable<Character>>();

// GET /api/characters/player/{playerId} - Get all characters owned by a player
characters.MapGet("/player/{playerId}", async (
        string playerId,
        ICharacterRepository repo) =>
{
    var result = await repo.GetByPlayerIdAsync(playerId);
    return Results.Ok(result);
})
    .WithName("GetCharactersByPlayer")
    .Produces<IEnumerable<Character>>();

// POST /api/characters - Create new character
characters.MapPost("/", async (
        [FromBody] Character character,
        ICharacterRepository repo) =>
{
    var created = await repo.CreateAsync(character);
    return Results.Created($"/api/characters/{created.Id}", created);
})
    .WithName("CreateCharacter")
    .Produces<Character>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// PUT /api/characters/{id} - Update existing character
characters.MapPut("/{id}", async (
        string id,
        [FromBody] Character character,
        ICharacterRepository repo) =>
{
    if (id != character.Id)
    {
        return Results.BadRequest("ID mismatch");
    }

    var existing = await repo.GetByIdAsync(id);
    if (existing is null)
    {
        return Results.NotFound();
    }

    var updated = await repo.UpdateAsync(character);
    return Results.Ok(updated);
})
    .WithName("UpdateCharacter")
    .Produces<Character>()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status400BadRequest);

// DELETE /api/characters/{id} - Delete character
characters.MapDelete("/{id}", async (
        string id,
        ICharacterRepository repo) =>
{
    var success = await repo.DeleteAsync(id);
    return success ? Results.NoContent() : Results.NotFound();
})
    .WithName("DeleteCharacter")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

#endregion

#region World Endpoints
// ============================================================================
// WORLD ENDPOINTS
// ============================================================================

var worlds = app.MapGroup("/api/worlds")
    .WithTags("Worlds");

// GET /api/worlds - List all worlds with pagination
worlds.MapGet("/", async (
        IWorldRepository repo,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50) =>
{
    var result = await repo.GetAllAsync(skip, take);
    return Results.Ok(result);
})
    .WithName("GetAllWorlds")
    .Produces<IEnumerable<World>>();

// GET /api/worlds/templates - Get world templates
worlds.MapGet("/templates", async (
        IWorldRepository repo) =>
{
    var result = await repo.GetTemplatesAsync();
    return Results.Ok(result);
})
    .WithName("GetWorldTemplates")
    .Produces<IEnumerable<World>>();

// GET /api/worlds/{id} - Get world by ID
worlds.MapGet("/{id}", async (
        string id,
        IWorldRepository repo) =>
{
    var world = await repo.GetByIdAsync(id);
    return world is not null ? Results.Ok(world) : Results.NotFound();
})
    .WithName("GetWorldById")
    .Produces<World>()
    .Produces(StatusCodes.Status404NotFound);

// GET /api/worlds/{id}/locations - Get all locations in a world
worlds.MapGet("/{id}/locations", async (
        string id,
        IWorldRepository repo) =>
{
    var world = await repo.GetByIdAsync(id);
    if (world is null)
    {
        return Results.NotFound();
    }
    return Results.Ok(world.Locations);
})
    .WithName("GetWorldLocations")
    .Produces<IEnumerable<Location>>()
    .Produces(StatusCodes.Status404NotFound);

// GET /api/worlds/{id}/npcs - Get all NPCs in a world
worlds.MapGet("/{id}/npcs", async (
        string id,
        IWorldRepository repo) =>
{
    var world = await repo.GetByIdAsync(id);
    if (world is null)
    {
        return Results.NotFound();
    }
    return Results.Ok(world.NPCs);
})
    .WithName("GetWorldNPCs")
    .Produces<IEnumerable<NPC>>()
    .Produces(StatusCodes.Status404NotFound);

// GET /api/worlds/{id}/quests - Get all quests in a world
worlds.MapGet("/{id}/quests", async (
        string id,
        IWorldRepository repo) =>
{
    var world = await repo.GetByIdAsync(id);
    if (world is null)
    {
        return Results.NotFound();
    }
    return Results.Ok(world.Quests);
})
    .WithName("GetWorldQuests")
    .Produces<IEnumerable<Quest>>()
    .Produces(StatusCodes.Status404NotFound);

// POST /api/worlds - Create new world
worlds.MapPost("/", async (
        [FromBody] World world,
        IWorldRepository repo) =>
{
    var created = await repo.CreateAsync(world);
    return Results.Created($"/api/worlds/{created.Id}", created);
})
    .WithName("CreateWorld")
    .Produces<World>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// PUT /api/worlds/{id} - Update existing world
worlds.MapPut("/{id}", async (
        string id,
        [FromBody] World world,
        IWorldRepository repo) =>
{
    if (id != world.Id)
    {
        return Results.BadRequest("ID mismatch");
    }

    var existing = await repo.GetByIdAsync(id);
    if (existing is null)
    {
        return Results.NotFound();
    }

    var updated = await repo.UpdateAsync(world);
    return Results.Ok(updated);
})
    .WithName("UpdateWorld")
    .Produces<World>()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status400BadRequest);

// DELETE /api/worlds/{id} - Delete world
worlds.MapDelete("/{id}", async (
        string id,
        IWorldRepository repo) =>
{
    var success = await repo.DeleteAsync(id);
    return success ? Results.NoContent() : Results.NotFound();
})
    .WithName("DeleteWorld")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);
#endregion

#region Narrative Endpoints
// ============================================================================
// NARRATIVE ENDPOINTS
// ============================================================================

var narratives = app.MapGroup("/api/narratives")
    .WithTags("Narratives");

// GET /api/narratives/{id} - Get narrative by ID
narratives.MapGet("/{id}", async (
        string id,
        INarrativeRepository repo) =>
{
    var narrative = await repo.GetByIdAsync(id);
    return narrative is not null ? Results.Ok(narrative) : Results.NotFound();
})
    .WithName("GetNarrativeById")
    .Produces<Narrative>()
    .Produces(StatusCodes.Status404NotFound);

// GET /api/narratives/campaign/{campaignId} - Get narratives for a campaign
narratives.MapGet("/campaign/{campaignId}", async (
        string campaignId,
        INarrativeRepository repo,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50) =>
{
    var result = await repo.GetByCampaignIdAsync(campaignId, take);
    return Results.Ok(result);
})
    .WithName("GetNarrativesByCampaign")
    .Produces<IEnumerable<Narrative>>();

// GET /api/narratives/campaign/{campaignId}/character/{characterId} - Get narratives visible to a character
narratives.MapGet("/campaign/{campaignId}/character/{characterId}", async (
        string campaignId,
        string characterId,
        INarrativeRepository repo,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50) =>
{
    var result = await repo.GetVisibleToCharacterAsync(campaignId, characterId, skip, take);
    return Results.Ok(result);
})
    .WithName("GetNarrativesVisibleToCharacter")
    .Produces<IEnumerable<Narrative>>();

// POST /api/narratives - Create new narrative
narratives.MapPost("/", async (
        [FromBody] Narrative narrative,
        INarrativeRepository repo) =>
{
    var created = await repo.CreateAsync(narrative);
    return Results.Created($"/api/narratives/{created.Id}", created);
})
    .WithName("CreateNarrative")
    .Produces<Narrative>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// POST /api/narratives/batch - Create multiple narratives
narratives.MapPost("/batch", async (
        [FromBody] IEnumerable<Narrative> narrativeList,
        INarrativeRepository repo) =>
{
    var created = await repo.CreateBatchAsync(narrativeList);
    return Results.Created("/api/narratives/batch", created);
})
    .WithName("CreateNarrativesBatch")
    .Produces<IEnumerable<Narrative>>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// DELETE /api/narratives/campaign/{campaignId}/cleanup - Clean up old narratives
narratives.MapDelete("/campaign/{campaignId}/cleanup", async (
        string campaignId,
        INarrativeRepository repo,
        [FromQuery] int keepCount = 1000) =>
{
    var deletedCount = await repo.DeleteOldEntriesAsync(campaignId, keepCount);
    return Results.Ok(new { DeletedCount = deletedCount });
})
    .WithName("CleanupOldNarratives")
    .Produces<object>();

#endregion

#region Game State Endpoints

// ============================================================================
// GAME STATE ENDPOINTS
// ============================================================================

var gameStates = app.MapGroup("/api/gamestate")
    .WithTags("GameState").WithHttpLogging(HttpLoggingFields.All);

// GET /api/gamestate/{campaignId} - Get complete game state for a campaign
gameStates.MapGet("/{campaignId}", async (
        string campaignId,
        IGameStateManager stateManager) =>
{
    var state = await stateManager.GetCampaignStateAsync(campaignId);
    return state is null ? Results.NotFound() : Results.Ok(state);
})
    .WithName("GetGameState")
    .Produces<GameState>()
    .Produces(StatusCodes.Status404NotFound);

// POST /api/gamestate/{campaignId}/save - Force save game state to persistent storage
gameStates.MapPost("/{campaignId}/save", async (
        string campaignId,
        IGameStateManager stateManager) =>
{
    var success = await stateManager.SaveStateAsync(campaignId);
    return success ? Results.Ok() : Results.Problem("Failed to save game state");
})
    .WithName("SaveGameState")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status500InternalServerError);

#endregion

#region Sessions

// ============================================================================
// SESSION ENDPOINTS
// ============================================================================

var sessions = app.MapGroup("/api/sessions")
    .WithTags("Sessions");

// POST /api/sessions - Create new session
sessions.MapPost("/", async (
        [FromBody] CreateSessionRequest request,
        ISessionStateManager sessionManager) =>
{
    var session = await sessionManager.CreateSessionAsync(request.SessionType, request.PlayerId);
    return Results.Created($"/api/sessions/{session.SessionId}", session);
})
    .WithName("CreateSession")
    .Produces<SessionState>(StatusCodes.Status201Created);

// GET /api/sessions/{id} - Get session by ID
sessions.MapGet("/{id}", async (
        string id,
        ISessionStateManager sessionManager) =>
{
    var session = await sessionManager.GetSessionStateAsync(id);
    return session is not null ? Results.Ok(session) : Results.NotFound();
})
    .WithName("GetSessionById")
    .Produces<SessionState>()
    .Produces(StatusCodes.Status404NotFound);

// DELETE /api/sessions/{id} - Delete session
sessions.MapDelete("/{id}", async (
        string id,
        ISessionStateManager sessionManager) =>
{
    await sessionManager.DeleteSessionAsync(id);
    return Results.NoContent();
})
    .WithName("DeleteSession")
    .Produces(StatusCodes.Status204NoContent);

// POST /api/sessions/{id}/complete - Complete session
sessions.MapPost("/{id}/complete", async (
        string id,
        [FromBody] CompleteSessionRequest request,
        ISessionStateManager sessionManager) =>
{
    await sessionManager.CompleteSessionAsync(id, request.ResultEntityId);
    return Results.Ok();
})
    .WithName("CompleteSession")
    .Produces(StatusCodes.Status200OK);

// POST /api/sessions/{id}/finalize-character - Finalize and save character
sessions.MapPost("/{id}/finalize-character", async (
        string id,
        ISessionCompletionService completionService) =>
{
    try
    {
        var characterId = await completionService.FinalizeCharacterAsync(id);
        return Results.Ok(new { CharacterId = characterId });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
    .WithName("FinalizeCharacter")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

// POST /api/sessions/{id}/finalize-world - Finalize and save world
sessions.MapPost("/{id}/finalize-world", async (
        string id,
        ISessionCompletionService completionService) =>
{
    try
    {
        var worldId = await completionService.FinalizeWorldAsync(id);
        return Results.Ok(new { WorldId = worldId });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
    .WithName("FinalizeWorld")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

// POST /api/sessions/{id}/finalize-campaign - Finalize and save campaign
sessions.MapPost("/{id}/finalize-campaign", async (
        string id,
        ISessionCompletionService completionService) =>
{
    try
    {
        var campaignId = await completionService.FinalizeCampaignAsync(id);
        return Results.Ok(new { CampaignId = campaignId });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
    .WithName("FinalizeCampaign")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);
#endregion

app.Run();
