using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgenticRpg.DiceRoller.Models;

public static class DieRollerExtensions
{
    public static IEnumerable<AITool> GetDiceRollerTools(this IRollDiceService diceService)
    {
        var tools = new DieRollerTools(diceService);
        return
        [
            //AIFunctionFactory.Create(tools.RollDie),
            AIFunctionFactory.Create(tools.RollNonPlayerCharacterDice),
            AIFunctionFactory.Create(tools.RollPlayerDice)
        ];
    }
}