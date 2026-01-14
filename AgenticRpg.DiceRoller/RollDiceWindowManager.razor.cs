using AgenticRpg.DiceRoller.Models;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.DiceRoller;

public partial class RollDiceWindowManager : IDisposable
{
    [Inject]
    private IRollDiceService RollDiceService { get; set; } = default!;
    
    private Dictionary<Guid, ActiveWindowState> _activeWindows = new();

    protected override void OnInitialized()
    {
        RollDiceService.OnDiceRollRequest += HandleOpenRequest;
        RollDiceService.OnDiceRollComplete += HandleCloseRequest;
    }

    private void HandleOpenRequest(Guid modalId, DiceRollComponentType type, RollDiceParameters? parameters, RollDiceWindowOptions? options)
    {
        var windowState = new ActiveWindowState
        {
            ModalId = modalId,
            Type = type,
            Parameters = parameters,
            Options = options ?? new RollDiceWindowOptions(),
            Location = options?.Location ?? Location.Center
        };
        
        _activeWindows[modalId] = windowState;
        InvokeAsync(StateHasChanged);
    }

    private void HandleCloseRequest(Guid modalId, RollDiceResults? results)
    {
        if (_activeWindows.Remove(modalId))
        {
            InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        RollDiceService.OnDiceRollRequest -= HandleOpenRequest;
        RollDiceService.OnDiceRollComplete -= HandleCloseRequest;
    }

    private class ActiveWindowState
    {
        public Guid ModalId { get; set; }
        public DiceRollComponentType Type { get; set; }
        public RollDiceParameters? Parameters { get; set; }
        public RollDiceWindowOptions Options { get; set; } = new();
        public Location Location { get; set; }
    }
}
