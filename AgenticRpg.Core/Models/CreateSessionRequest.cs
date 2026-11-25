using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Models;

public class CreateSessionRequest
{
    public CreateSessionRequest(SessionType sessionType, string playerId)
    {
        SessionType = sessionType;
        PlayerId = playerId;
    }

    public CreateSessionRequest()
    {

    }

    public SessionType SessionType { get; set; }
    public string PlayerId { get; set; }
    public string? WorldId { get; set; }
    public string? CampaignId { get; set; }
    public bool StartNewAdventure { get; set; }
    public string? QuickStarter { get; set; }
}
