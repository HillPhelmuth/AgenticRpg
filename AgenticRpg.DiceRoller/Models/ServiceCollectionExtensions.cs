using Microsoft.Extensions.DependencyInjection;

namespace AgenticRpg.DiceRoller.Models;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRollDisplayService(this IServiceCollection services)
    {
        return services.AddSingleton<IRollDiceService, RollDiceClientService>();
    }

    
}
