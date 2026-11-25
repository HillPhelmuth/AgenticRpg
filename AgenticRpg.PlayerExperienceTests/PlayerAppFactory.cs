using System.Security.Claims;
using System.Text.Encodings.Web;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRpg.PlayerExperienceTests;

internal sealed class PlayerAppFactory : WebApplicationFactory<Program>
{
    public TestRuntime Fakes { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStaticWebAssets();
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CosmosDBConnection"] = "AccountEndpoint=https://localhost:8081/;AccountKey=" + Convert.ToBase64String(new byte[64]) + ";",
            ["Auth0:Domain"] = "test.example.com", ["Auth0:ClientId"] = "test-client"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "PlayerTest";
                options.DefaultChallengeScheme = "PlayerTest";
                options.DefaultForbidScheme = "PlayerTest";
            }).AddScheme<AuthenticationSchemeOptions, PlayerAuthentication>("PlayerTest", _ => { });
            services.AddSingleton<IRecoveryRepository>(Fakes.Recovery);
            services.AddSingleton(Fakes.Characters.Object);
            services.AddSingleton(Fakes.Worlds.Object);
            services.AddSingleton(Fakes.NarrativesRepository.Object);
            services.AddSingleton(Fakes.Campaigns.Object);
            services.AddSingleton(Fakes.States.Object);
            services.AddSingleton(Fakes.Threads.Object);
            services.AddSingleton<ICampaignQuestPreparation>(Moq.Mock.Of<ICampaignQuestPreparation>());
            services.AddSingleton(Fakes.Locks);
            services.AddSingleton<ISessionStateManager>(Fakes.Sessions);
            services.AddSingleton(Fakes.Connections);
            services.AddSingleton<IGameMessageProcessor>(Fakes.Processor);
            // AI providers are deliberately absent from these tests; the real hub/runtime contracts execute.
            services.AddTransient(sp => new GameHub(sp.GetRequiredService<IGameStateManager>(),
                sp.GetRequiredService<ISessionStateManager>(), null!, null!, sp.GetRequiredService<RollDiceHubService>(),
                sp.GetRequiredService<ILogger<GameHub>>(), sp.GetRequiredService<GameConnectionRegistry>(),
                sp.GetRequiredService<IPlayerStateProjector>(), sp.GetRequiredService<PlayerStateBroadcaster>(),
                sp.GetRequiredService<GameRuntimeCoordinator>(), sp.GetRequiredService<ICharacterRepository>(),
                sp.GetRequiredService<ICampaignRepository>(), sp.GetRequiredService<RollDiceHubService>(),
                sp.GetRequiredService<ISessionCompletionService>(), sp.GetRequiredService<ScopeOperationLocks>()));
        });
    }
    public HttpClient Client(string? player = null)
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        if (player is not null) client.DefaultRequestHeaders.Add("Cookie", "PlaytestPlayer=" + player);
        return client;
    }
}

internal sealed class PlayerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var player = Request.Cookies["PlaytestPlayer"];
        if (string.IsNullOrWhiteSpace(player)) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, player)], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
