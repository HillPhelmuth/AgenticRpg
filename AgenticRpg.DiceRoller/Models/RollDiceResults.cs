using System.Data.Common;
using System.Text.Json.Serialization;

namespace AgenticRpg.DiceRoller.Models;

public class RollDiceResults
{
    public RollDiceResults()
    {
        //Parameters = new RollDiceParameters();
    }

    public RollDiceResults(bool success)
    {
        IsSuccess = success;
        //Parameters = parameters ?? new RollDiceParameters();
    }


    [JsonPropertyName("results")]
    public DieRollResults? Results { get; set; }
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    /// <summary>
    /// The session or campaign identifier that initiated the roll. Used by the server to route results.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Unique request identifier so the server can match responses to pending dice roll tasks.
    /// </summary>
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    //public RollDiceParameters Parameters { get; set; }

    public static RollDiceResults Empty(bool success)
    {
        return new RollDiceResults(success);
    }
}

public class Rootobject
{
    public Class1[] Property1 { get; set; }
}

public class Class1
{
    public Results results { get; set; }
    public bool isSuccess { get; set; }
    public string sessionId { get; set; }
    public string requestId { get; set; }
}

public class Results
{
    public int[] rollResults { get; set; }
    public int total { get; set; }
}

