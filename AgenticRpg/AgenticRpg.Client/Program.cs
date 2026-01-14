using AgenticRpg.Client.Services;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var services = builder.Services;
services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
});
// Configure HttpClient for API calls
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7179";
services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
services.AddCascadingAuthenticationState();
// Register API services
services.AddScoped<ICampaignService, CampaignService>();
services.AddScoped<ICharacterService, CharacterService>();
services.AddScoped<IWorldService, WorldService>();
services.AddScoped<IGameStateService, GameStateService>();

// Register SignalR hub service
services.AddSingleton<IGameHubService, GameHubService>();

// Register dice roller service
services.AddRollDisplayService();
await builder.Build().RunAsync();
