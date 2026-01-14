using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.DiceRoller.Components;
public partial class DiceRoller
{
    [Parameter]
    public Guid? ModalId { get; set; }
    
    [Parameter]
    public int NumberOfRolls { get; set; } = 1;

    private int _previousNumberOfRolls;
    [Parameter]
    public DieType DieType { get; set; } = DieType.D20;

    private DieRoller Component { set => _components.Add(value); }

    List<DieRoller> _components = [];

    [Inject] 
    public IRollDiceService RollDiceService { get; set; } = default!;    
    
    private List<int> _rolls = [];
    [Parameter]
    public int Duration { get; set; } = 2000;

    [Parameter] 
    public bool IsStandalone { get; set; } = false;
    [Parameter]
    public bool IsManual { get; set; } = true;
    [Parameter]
    public EventCallback<RollDiceResults> OnRollDiceResults { get; set; }
    protected override void OnInitialized()
    {
        _previousNumberOfRolls = NumberOfRolls;
        base.OnInitialized();
    }
    protected override Task OnParametersSetAsync()
    {
        if (IsStandalone && _previousNumberOfRolls > NumberOfRolls)
        {
            var componentsToRemoveFromEnd = _previousNumberOfRolls - NumberOfRolls;
            for (var i = 0; i < componentsToRemoveFromEnd; i++)
            {
                _components.RemoveAt(_components.Count - 1);
            }
            _previousNumberOfRolls = NumberOfRolls;
        }
        return base.OnParametersSetAsync();
    }

    private async void HandleValueUpdate(int value)
    {
        try
        {
            _rolls.Add(value);
            if (_rolls.Count < NumberOfRolls)
            {
                return;
            }
            await Task.Delay(Duration);
            if (!IsStandalone)
            {
                if (ModalId.HasValue)
                {
                    RollDiceService.Close(ModalId.Value, new RollDiceResults(true){ Results = new DieRollResults(_rolls, _rolls.Sum()) });
                }
            }
            else 
                await OnRollDiceResults.InvokeAsync(new RollDiceResults(true){ Results = new DieRollResults(_rolls, _rolls.Sum()) });
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error occurred while rolling dice: {e}");
        }
    }
    private bool _rolling;
    private async Task RollAll()
    {
        if (_rolling)
            return;
        _rolls.Clear();
        _rolling = true;
        var dieRollers = IsStandalone ? _components : _components.Where(x => !x.HasRolled);
        var tasks = dieRollers.Select(component => component.RollDice()).ToList();
        await Task.WhenAll(tasks);
        StateHasChanged();
        await Task.Delay(Duration);
        if (!IsStandalone && ModalId.HasValue)
            RollDiceService.Close(ModalId.Value, new RollDiceResults(true) { Results = new DieRollResults(_rolls, _rolls.Sum()) });
        _rolling = false;
    }
}
