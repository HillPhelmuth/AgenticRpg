using AgenticRpg.DiceRoller.Models;
using System.Linq;

namespace AgenticRpg.Client.Services;

/// <summary>
/// Service that coordinates between SignalR dice roll requests and the RollDiceWindow UI
/// </summary>
public interface ISignalRDiceRollerService
{
    Task HandleDiceRollRequestAsync(DiceRollRequest request);
}

public class SignalRDiceRollerService : ISignalRDiceRollerService
{
    private readonly IRollDiceService _rollDiceService;
    private readonly IGameHubService _gameHubService;
    private readonly ILogger<SignalRDiceRollerService> _logger;

    public SignalRDiceRollerService(
        IRollDiceService rollDiceService,
        IGameHubService gameHubService,
        ILogger<SignalRDiceRollerService> logger)
    {
        _rollDiceService = rollDiceService;
        _gameHubService = gameHubService;
        _logger = logger;
    }

    public async Task HandleDiceRollRequestAsync(DiceRollRequest request)
    {
        _logger.LogInformation("Handling dice roll request {RequestId}: {Reason}", 
            request.RequestId, request.ReasonForRolling);

        try
        {
            // Determine window location based on whether it's manual or automatic
            var location = request.IsManual ? Location.Center : Location.TopRight;
            var minWidth = request.IsManual ? "50vw" : "30vw";

            // Configure the dice rolling window
            var windowOptions = new RollDiceWindowOptions
            {
                Title = $"Roll {request.NumberOfDice}{request.DieType} for {request.ReasonForRolling}",
                Location = location,
                Style = $"width:max-content;min-width:{minWidth};height:max-content"
            };

            // Configure the dice rolling parameters
            var parameters = new RollDiceParameters
            {
                DieType = request.DieType,
                NumberOfRolls = request.NumberOfDice,
                IsManual = request.IsManual
            };

            var sessionId = request.SessionId ?? request.CampaignId ?? string.Empty;

            // Open the dice window and await the result
            var rawResults = await _rollDiceService.RequestDiceRoll(sessionId, parameters, windowOptions, request.NumberOfRollWindows)
                .ConfigureAwait(false);

            var results = rawResults?.Select(r => r ?? RollDiceResults.Empty(false)).ToList() ?? new List<RollDiceResults>();

            if (request.WindowRequestIds is { Count: > 0 } windowIds && windowIds.Count == results.Count)
            {
                for (var i = 0; i < results.Count; i++)
                {
                    results[i].RequestId = windowIds[i];
                    results[i].SessionId = sessionId;
                }
            }
            else
            {
                foreach (var result in results)
                {
                    result.RequestId = request.RequestId;
                    result.SessionId = sessionId;
                }
            }

            foreach (var result in results)
            {
                // Apply drop lowest if requested by mutating parameters before sending
                if (request.DropLowest &&
                    result.Results?.RollResults is { Count: > 1 } rollList)
                {
                    var adjusted = rollList.OrderBy(x => x).Skip(1).ToList();
                    result.Results.RollResults = adjusted;
                    result.Results.Total = adjusted.Sum();
                }
            }

            await _gameHubService.SubmitDiceRollResultAsync(results).ConfigureAwait(false);

            var submittedRolls = results.Select(x => x.Results?.Total);
            _logger.LogInformation("Dice roll result submitted for request {RequestId}: {Rolls}",
                request.RequestId, string.Join(", ", submittedRolls));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling dice roll request {RequestId}", request.RequestId);

            try
            {
                if (request.WindowRequestIds is { Count: > 0 } windowIds)
                {
                    var sessionId = request.SessionId ?? request.CampaignId ?? string.Empty;
                    var failures = windowIds
                        .Select(windowId => new RollDiceResults(false) { RequestId = windowId, SessionId = sessionId })
                        .ToList();

                    await _gameHubService.SubmitDiceRollResultAsync(failures).ConfigureAwait(false);
                }
                else
                {
                    // Send error result back to server
                    var errorResult = RollDiceResults.Empty(false);
                    errorResult.RequestId = request.RequestId;
                    errorResult.SessionId = request.SessionId ?? request.CampaignId ?? string.Empty;

                    await _gameHubService.SubmitDiceRollResultAsync([errorResult]).ConfigureAwait(false);
                }
            }
            catch (Exception submitEx)
            {
                _logger.LogError(submitEx, "Error submitting error result for request {RequestId}", request.RequestId);
            }
        }
    }
}
