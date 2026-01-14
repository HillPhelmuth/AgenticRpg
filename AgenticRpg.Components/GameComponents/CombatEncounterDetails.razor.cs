using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Microsoft.AspNetCore.Components;

namespace AgenticRpg.Components.GameComponents;

public partial class CombatEncounterDetails
{
    [Parameter] public CombatEncounter? Encounter { get; set; }

    [Parameter] public string CssClass { get; set; } = string.Empty;

    private bool ShowMonsterModal { get; set; }
    private RpgMonster? SelectedMonster { get; set; }

    private void SelectMonster(RpgMonster monster)
    {
        if (monster.SpecialAttacks.Any() || !string.IsNullOrEmpty(monster.DefaultAttack.Name))
        {
            SelectedMonster = monster;
            ShowMonsterModal = true;
        }
    }

    private void CloseModal()
    {
        ShowMonsterModal = false;
        SelectedMonster = null;
    }
}