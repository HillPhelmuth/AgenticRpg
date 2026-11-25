using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Helpers;
using AgenticRpg.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace AgenticRpg.PlayerExperienceTests;

public class DependencyGraphTests
{
    [Fact]
    public void ProductionAgentGraphHasNoCircularFeedbackDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSignalR();
        services.AddSingleton(Moq.Mock.Of<Microsoft.Extensions.Hosting.IHostApplicationLifetime>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new AgentConfiguration());
        services.AddSingleton<IAgentContextProvider, AgentContextProvider>();
        using var cosmos = new CosmosClient("https://localhost:8081", Convert.ToBase64String(new byte[64]));
        services.AddSingleton(cosmos);
        services.AddCoreRpgServices(); services.AddRollDiceHubService();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
