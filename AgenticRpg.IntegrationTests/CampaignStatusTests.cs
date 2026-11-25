using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Models.Game;

namespace AgenticRpg.IntegrationTests;

/// <summary>
/// Verifies campaign lifecycle status transitions derived from membership and connectivity state.
/// </summary>
public sealed class CampaignStatusTests
{
    /// <summary>
    /// Returns setup before any player has joined the campaign.
    /// </summary>
    [Fact]
    public void GetCampaignStatusReturnsSetupWhenNoPlayersHaveJoined()
    {
        var campaign = new Campaign();

        Assert.Equal(CampaignStatus.Setup, campaign.Status);
    }

    /// <summary>
    /// Returns lobby after players have joined but before the campaign has started.
    /// </summary>
    [Fact]
    public void GetCampaignStatusReturnsLobbyWhenPlayersHaveJoinedButCampaignHasNotStarted()
    {
        var campaign = new Campaign();
        campaign.MarkPlayerJoined("player-1", "character-1");

        Assert.Equal(CampaignStatus.Lobby, campaign.Status);
    }

    /// <summary>
    /// Returns active when the campaign has started and a player is connected.
    /// </summary>
    [Fact]
    public void GetCampaignStatusReturnsActiveWhenCampaignHasStartedAndPlayersAreConnected()
    {
        var campaign = new Campaign();
        campaign.MarkPlayerJoined("player-1", "character-1");
        campaign.MarkStarted();
        campaign.MarkPlayerConnected("player-1");

        Assert.Equal(CampaignStatus.Active, campaign.Status);
    }

    /// <summary>
    /// Returns paused when the campaign has started and no players remain connected.
    /// </summary>
    [Fact]
    public void GetCampaignStatusReturnsPausedWhenCampaignHasStartedAndNoPlayersAreConnected()
    {
        var campaign = new Campaign();
        campaign.MarkPlayerJoined("player-1", "character-1");
        campaign.MarkStarted();

        Assert.Equal(CampaignStatus.Paused, campaign.Status);
    }

    /// <summary>
    /// Preserves campaign membership when a player disconnects unexpectedly.
    /// </summary>
    [Fact]
    public void PlayerDisconnectPreservesMembershipButClearsConnectionState()
    {
        var gameState = new GameState();
        gameState.PlayerJoinGame("player-1", "character-1", "Aela", "connection-1");
        gameState.SetReadyStatus("player-1", "character-1", "Aela", true, "connection-1");
        gameState.Campaign.MarkStarted();

        gameState.PlayerDisconnect("player-1");

        Assert.Contains("player-1", gameState.Campaign.PlayerIds);
        Assert.Contains("character-1", gameState.Campaign.CharacterIds);
        Assert.DoesNotContain("player-1", gameState.Campaign.ActiveAndConnectedPlayerIds);
        Assert.False(gameState.PlayerReadyStatuses["player-1"].IsReady);
        Assert.Null(gameState.PlayerReadyStatuses["player-1"].ConnectionId);
        Assert.Equal(CampaignStatus.Paused, gameState.Campaign.Status);
    }

    /// <summary>
    /// Removes campaign membership and the controlled character on explicit leave.
    /// </summary>
    [Fact]
    public void PlayerLeaveGameRemovesMembershipAndCharacter()
    {
        var gameState = new GameState
        {
            Characters =
            [
                new Character
                {
                    Id = "character-1",
                    PlayerId = "player-1",
                    CampaignId = "campaign-1",
                    Name = "Aela"
                }
            ]
        };

        gameState.PlayerJoinGame("player-1", "character-1", "Aela", "connection-1");

        gameState.PlayerLeaveGame("player-1", "character-1");

        Assert.DoesNotContain("player-1", gameState.Campaign.PlayerIds);
        Assert.DoesNotContain("character-1", gameState.Campaign.CharacterIds);
        Assert.Empty(gameState.Characters);
        Assert.Equal(CampaignStatus.Setup, gameState.Campaign.Status);
    }
}