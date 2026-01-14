namespace AgenticRpg.DiceRoller.Models;

public interface IRollDiceService
{
    bool IsOpen { get; set; }
    List<DieRoleModalReference> DieRollReferences { get; set; }
    event Action<Guid, RollDiceResults?>? OnDiceRollComplete;
    event Action<Guid, DiceRollComponentType, RollDiceParameters?, RollDiceWindowOptions?>? OnDiceRollRequest;
    Task<List<RollDiceResults?>> RequestDiceRoll(string sessionId, RollDiceParameters? parameters = null,
        RollDiceWindowOptions? options = null, int numberOfRollWindows = 1);
    void CloseSelf(Guid modalId, RollDiceResults? results = null);
    void Close(Guid modalId, RollDiceResults? result);
}