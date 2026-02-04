using AgenticRpg.Client.Services;
using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var services = builder.Services;
//services.AddMsalAuthentication(options =>
//{
//    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
//    var defaultScopes = builder.Configuration.GetSection("AzureAd:DefaultScopes").Get<string[]>();
//    if (defaultScopes is not null && defaultScopes.Length > 0)
//    {
//        foreach (var scope in defaultScopes)
//        {
//            options.ProviderOptions.DefaultAccessTokenScopes.Add(scope);
//        }
//    }
//});
// Configure HttpClient for API calls (same origin as host)
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
var apiBaseUrl = builder.HostEnvironment.BaseAddress;
services.AddHttpClient("ApiClient", client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
    });
services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));
services.AddCascadingAuthenticationState();
// Register API services
services.AddScoped<ICampaignService, CampaignService>();
services.AddScoped<ICharacterService, CharacterService>();
services.AddScoped<IWorldService, WorldService>();
services.AddScoped<IGameStateService, GameStateService>();

// Register SignalR hub service
services.AddSingleton<IGameHubService, GameHubService>();

// Register JS interop helpers
services.AddScoped<AudioInterop>();

// Register dice roller service
services.AddRollDisplayService();
await builder.Build().RunAsync();
