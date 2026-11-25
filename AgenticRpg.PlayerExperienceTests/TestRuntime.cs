using System.Collections.Concurrent;
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Threads;
using AgenticRpg.Core.Hubs;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

internal sealed class TestRuntime
{
    public Mock<ICharacterRepository> Characters { get; } = new();
    public Mock<INarrativeRepository> NarrativesRepository { get; } = new();
    public Mock<IWorldRepository> Worlds { get; } = new();
    public Mock<ICampaignRepository> Campaigns { get; } = new();
    public Mock<IGameStateManager> States { get; } = new();
    public Mock<IAgentSessionStore> Threads { get; } = new();
    public Mock<IHubContext<GameHub>> Hub { get; } = new();
    public Mock<IHubClients> Clients { get; } = new();
    public ConcurrentQueue<(string Target, string Method, object?[] Args)> Events { get; } = new();
    public GameConnectionRegistry Connections { get; } = new();
    public IPlayerStateProjector Projector { get; } = new PlayerStateProjector();
    public ScopeOperationLocks Locks { get; } = new();
    public SessionStateManager Sessions { get; }
    public PlayerStateBroadcaster Broadcaster { get; }
    public GameRuntimeCoordinator Runtime { get; }
    public SessionCompletionService Completion { get; }
    public ControlledProcessor Processor { get; } = new();
    public GameState State { get; } = new() { Campaign = new() { Id = "campaign", OwnerId = "alice", InvitedUserIds = ["bob"] } };
    public Dictionary<string, Character> CharacterRecords { get; } = [];
    public Dictionary<string, World> WorldRecords { get; } = [];
    public Dictionary<string, Campaign> CampaignRecords { get; } = [];
    private readonly Dictionary<string, GameState> _campaignStates = [];
    public bool FailWrites { get; set; }

    public IRecoveryRepository Recovery { get; } = new InMemoryRecoveryRepository();

    public TestRuntime()
    {
        NarrativesRepository.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => State.RecentNarratives.FirstOrDefault(n => n.Id == id));
        NarrativesRepository.Setup(r => r.GetByCampaignIdAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => State.RecentNarratives);
        NarrativesRepository.Setup(r => r.GetVisibleToCharacterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => State.RecentNarratives);
        Hub.SetupGet(h => h.Clients).Returns(Clients.Object);
        Clients.Setup(c => c.Group(It.IsAny<string>())).Returns((string group) => Proxy(group));
        Clients.Setup(c => c.Client(It.IsAny<string>())).Returns((string connection) => Proxy(connection));
        Clients.As<IHubClients<IClientProxy>>().Setup(c => c.Client(It.IsAny<string>())).Returns((string connection) => Proxy(connection));
        Sessions = new(NullLogger<SessionStateManager>.Instance, Hub.Object, Locks, Recovery);
        Broadcaster = new(Hub.Object, Connections, Projector);
        Runtime = new(Processor, Hub.Object, States.Object, Sessions, Broadcaster, NullLogger<GameRuntimeCoordinator>.Instance, Locks, recovery: Recovery);
        Completion = new(Sessions, Characters.Object, Worlds.Object, Campaigns.Object, Threads.Object, Locks, Hub.Object,
            NullLogger<SessionCompletionService>.Instance, Mock.Of<ICampaignQuestPreparation>());
        States.Setup(s => s.GetCampaignStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (id == State.CampaignId) return State;
                if (!CampaignRecords.TryGetValue(id, out var campaign)) return null;
                if (!_campaignStates.TryGetValue(id, out var game))
                    _campaignStates[id] = game = new() { Campaign = campaign, World = WorldRecords.GetValueOrDefault(campaign.WorldId) ?? new() };
                game.Characters = CharacterRecords.Values.Where(c => c.CampaignId == id).ToList();
                return game;
            });
        States.Setup(s => s.UpdateCampaignStateAsync(It.IsAny<GameState>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Characters.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => CharacterRecords.GetValueOrDefault(id));
        Characters.Setup(r => r.GetByPlayerIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => CharacterRecords.Values.Where(c => c.PlayerId == id).ToList());
        Characters.Setup(r => r.GetByCampaignIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => CharacterRecords.Values.Where(c => c.CampaignId == id).ToList());
        Characters.Setup(r => r.CreateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Character c, CancellationToken _) => { Write(); CharacterRecords[c.Id] = c; return c; });
        Characters.Setup(r => r.UpdateAsync(It.IsAny<Character>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Character c, CancellationToken _) => { Write(); CharacterRecords[c.Id] = c; return c; });
        Worlds.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => WorldRecords.GetValueOrDefault(id));
        Worlds.Setup(r => r.GetAllAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WorldRecords.Values.ToList());
        Worlds.Setup(r => r.CreateAsync(It.IsAny<World>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((World w, CancellationToken _) => { Write(); WorldRecords[w.Id] = w; return w; });
        Worlds.Setup(r => r.UpdateAsync(It.IsAny<World>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((World w, CancellationToken _) => { Write(); WorldRecords[w.Id] = w; return w; });
        Campaigns.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => id == State.CampaignId ? State.Campaign : CampaignRecords.GetValueOrDefault(id));
        Campaigns.Setup(r => r.CreateAsync(It.IsAny<Campaign>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Campaign c, CancellationToken _) => { Write(); CampaignRecords[c.Id] = c; return c; });
    }

    private void Write() { if (FailWrites) throw new InvalidOperationException("Repository unavailable"); }
    private ISingleClientProxy Proxy(string target)
    {
        var proxy = new Mock<ISingleClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback((string method, object?[] args, CancellationToken _) => Events.Enqueue((target, method, args)))
            .Returns(Task.CompletedTask);
        return proxy.Object;
    }
    public static Character Character(string id = "a", string player = "alice") => new()
    {
        Id = id, PlayerId = player, Name = player, Race = CharacterRace.Human, Class = CharacterClass.Warrior,
        CurrentHP = 12, CampaignId = "campaign",
        Skills = Skill.GetAllSkillsFromFile().Take(4).ToDictionary(s => s.Name, _ => 1)
    };
    public static List<Narrative> Narratives() =>
    [
        new() { CampaignId = "campaign", Id = "public", Content = "The party arrives.", Visibility = NarrativeVisibility.Global },
        new() { CampaignId = "campaign", Id = "gm", Content = "Hidden villain.", Visibility = NarrativeVisibility.GMOnly },
        new() { CampaignId = "campaign", Id = "a-secret", Content = "Alice's clue.", Visibility = NarrativeVisibility.CharacterSpecific, TargetCharacterId = "a" },
        new() { CampaignId = "campaign", Id = "b-secret", Content = "Bob's clue.", Visibility = NarrativeVisibility.CharacterSpecific, TargetCharacterId = "b" }
    ];
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}

internal sealed class ControlledProcessor : IGameMessageProcessor
{
    public ConcurrentQueue<PlayerMessageRequest> Requests { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource<AgentResponse>> Results { get; } = new();
    public Task<AgentResponse> ProcessAsync(PlayerMessageRequest request, bool isSession)
    {
        var result = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Results[request.ClientMessageId!] = result;
        Requests.Enqueue(request);
        return result.Task.WaitAsync(request.CancellationToken);
    }
    public void Complete(string id, List<string>? suggestions = null) => Results[id].SetResult(new() { Success = true,
        FormattedResponse = new() { MessageToPlayers = "Done", SuggestedActions = suggestions is null ? [] :
            [new() { Player = "alice", Suggestions = suggestions }, new() { Player = "bob", Suggestions = suggestions }] } });
}
