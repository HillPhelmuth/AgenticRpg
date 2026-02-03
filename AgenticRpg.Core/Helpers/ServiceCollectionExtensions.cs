using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Rules;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using AgenticRpg.DiceRoller.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgenticRpg.Core.Helpers;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRollDiceHubService()
        {
            return services.AddSingleton<IRollDiceService, RollDiceHubService>();
        }

        public IServiceCollection AddCoreRpgServices()
        {
            services.AddSingleton<ICampaignRepository, CampaignRepository>();
            services.AddSingleton<ICharacterRepository, CharacterRepository>();
            services.AddSingleton<IWorldRepository, WorldRepository>();
            services.AddSingleton<INarrativeRepository, NarrativeRepository>();
            services.AddSingleton<IGameStateManager, GameStateManager>();
            services.AddSingleton<ISessionStateManager, SessionStateManager>();
            services.AddSingleton<IGameStateRepository, GameStateRepository>();
            services.AddSingleton<VideoGenService>();
        
            // Register Core Services
            services.AddSingleton<CombatRulesEngine>();
            services.AddSingleton<AttributeCalculator>();
            services.AddSingleton<StatsCalculator>();

            // Shared agent thread store (scopeId + AgentType -> AgentThread)
            services.AddSingleton<IAgentThreadStore, InMemoryAgentThreadStore>();

            services.AddSingleton<ISessionCompletionService, SessionCompletionService>();
        
            // Register AI Agents
            services.AddSingleton<GameMasterAgent>();
            services.AddSingleton<CombatAgent>();
            services.AddSingleton<CharacterCreationAgent>();
            services.AddSingleton<CharacterManagerAgent>();
            services.AddSingleton<ShopKeeperAgent>();
            services.AddSingleton<WorldBuilderAgent>();

            // Register Agent Orchestration Service
            services.AddSingleton<AgentOrchestrationService>();
            return services;
        }
    }
}