using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.DiceRoller.Components;
public partial class DieRoller
{
    public int DieVal { get; set; } = 20;
    [Parameter]
    public DieType DieType { get; set; } = DieType.D20;

    [Parameter]
    public int CurrentValue { get; set; } = 1;
    [Parameter]
    public EventCallback<int> CurrentValueChanged { get; set; }

    [Parameter] 
    public bool IsManual { get; set; } = true;
    [Parameter]
    public bool IsStandalone { get; set; }

    private string _diceClass = "stopped";
    private string _rollingCss = "";
    private int _currentFace = 1;
    private Timer _rollTimer;
    public bool HasRolled { get; private set; }
    protected override async Task OnParametersSetAsync()
    {
        DieVal = DieType switch
        {
            DieType.D4 => 4,
            DieType.D6 => 6,
            DieType.D8 => 8,
            DieType.D10 => 10,
            DieType.D12 => 12,
            DieType.D20 => 20,
            _ => DieVal
        };
        if (!IsManual && !HasRolled)
        {
            await RollDice();
            HasRolled = true;
        }
        await base.OnParametersSetAsync();
    }
    protected override void OnInitialized()
    {

    }
    private bool _rollStarted;
    
    public async Task ManualRoll()
    {
        if (!IsManual) return;
        await RollDice();
    }
    public async Task RollDice()
    {
        if (HasRolled) return;
        // TEMP Console.WriteLine($"Roll {DieType} Initiated");
        if (_rollingCss == "rolling")
            return;
        _diceClass = "rolling";
        StateHasChanged();
        await Task.Delay(1);
        var nextValue = new Random().Next(1, DieVal + 1);
        //await connection.SendAsync("SendRolling", _rollingCss);
        await Task.Delay(3000);
        //SharedResult = DieRollService.Result;
        //await connection.SendAsync("SendRoll", SharedResult);
        _currentFace = nextValue;
        CurrentValue = _currentFace;
        await CurrentValueChanged.InvokeAsync(_currentFace);
        _diceClass = "stopped";
        HasRolled = !IsStandalone;
        StateHasChanged();
    }
}
