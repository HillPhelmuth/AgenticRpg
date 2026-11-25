using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Services;
using AgenticRpg.Core.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

// Opt-in host for the accompanying two-browser script. It uses the real application/client,
// test-only identities and repositories, and never calls paid AI services or production storage.
public class BrowserPlaytestHostTests
{
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RPG_BROWSER_PLAYTEST_SIGNAL"));

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set RPG_BROWSER_PLAYTEST_SIGNAL to start the opt-in two-browser host.")]
    public async Task HostForTwoBrowserPlaytest()
    {
        await using var app = new PlayerAppFactory();
        app.UseKestrel(0);
        var f = app.Fakes;
        f.CharacterRecords["a"] = TestRuntime.Character();
        f.CharacterRecords["b"] = TestRuntime.Character("b", "bob");
        f.State.Characters = f.CharacterRecords.Values.ToList();
        f.State.Campaign.Name = "Recovery playtest";
        f.State.Campaign.WorldId = "world";
        f.State.World = new() { Id = "world", Name = "Playtest world", CreatorId = "alice" };
        f.State.Campaign.PrimaryQuest = new() { Name = "Find the gate", Objectives = ["Find the key", "Open the gate"], CompletedObjectives = new() { ["Find the key"] = true } };
        f.State.World.NPCs = [new() { Id = "guide", Name = "Insufferable Guide" }];
        f.WorldRecords["world"] = f.State.World;
        f.State.RecentNarratives = TestRuntime.Narratives();
        f.Campaigns.Setup(r => r.GetByOwnerIdAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[] { f.State.Campaign }.Concat(f.CampaignRecords.Values));
        f.Campaigns.Setup(r => r.GetByInvitationCodeAsync("PLAYTEST", Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>())).ReturnsAsync(f.State.Campaign);
        using var client = app.Client("alice");
        var sessions = app.Services.GetRequiredService<ISessionStateManager>();
        using var stop = new CancellationTokenSource();
        var responder = Task.Run(async () =>
        {
            var seen = new HashSet<string>();
            while (!stop.IsCancellationRequested)
            {
                foreach (var request in f.Processor.Requests.Where(r => seen.Add(r.ClientMessageId!)))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                        await request.StatusCallback!(new() { Status = AgenticRpg.Core.Messaging.MessageProcessingStatus.Processing });
                        await request.StreamCallback!(new() { Token = "Beginning the action. ", AgentType = "GameMaster" });
                        await Task.Delay(1800, request.CancellationToken);
                        var session = await sessions.GetSessionStateAsync(request.CampaignId);
                        if (session is not null)
                        {
                            if (session.SessionType == SessionType.WorldBuilding)
                                session.Context.DraftWorld ??= new() { Name = "Generated playtest world" };
                            if (session.SessionType == SessionType.CharacterCreation)
                            {
                                session.Context.DraftCharacter ??= TestRuntime.Character("new-character");
                                session.Context.DraftCharacter.CampaignId = session.Context.CampaignId ?? string.Empty;
                            }
                            await sessions.UpdateSessionStateAsync(session);
                        }
                        await request.StreamCallback!(new() { Token = "The action is complete.", AgentType = "GameMaster" });
                        f.Processor.Complete(request.ClientMessageId!, session is null ? null :
                            ["I'll take Path 1: Quick Creation. Make me a sneaky rogue who stabs people.",
                             "Let's go with Path 2: Step-by-Step Creation. I want to make all the choices myself.",
                             "I'm feeling lazy. Path 1: Quick Creation. Surprise me with a complete character, you insufferable machine.",
                             "I am definitely a control freak. Let's do Path 2: Step-by-Step."]);
                        }
                        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested) { }
                    });
                }
                await Task.Delay(25);
            }
        });
        var signal = Environment.GetEnvironmentVariable("RPG_BROWSER_PLAYTEST_SIGNAL")!;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await File.WriteAllTextAsync(signal, address);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            while (File.Exists(signal)) await Task.Delay(100, timeout.Token);
        }
        finally { stop.Cancel(); await responder; }
        Assert.NotEmpty(f.Processor.Requests);
    }
}
