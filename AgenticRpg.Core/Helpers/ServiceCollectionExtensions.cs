using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using AgenticRpg.Core.Services;

namespace AgenticRpg.Core.Helpers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRollDiceHubService(this IServiceCollection services)
    {
        return services.AddSingleton<IRollDiceService, RollDiceHubService>();
    }
}