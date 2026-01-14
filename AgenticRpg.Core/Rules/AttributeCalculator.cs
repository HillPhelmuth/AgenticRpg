using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Enums;

namespace AgenticRpg.Core.Rules;

/// <summary>
/// Calculates attribute scores and modifiers based on game rules
/// </summary>
public class AttributeCalculator
{
    /// <summary>
    /// Calculates the modifier for an attribute score
    /// Standard formula: (score - 10) / 2
    /// </summary>
    /// <param name="attributeScore">The attribute score (typically 1-20)</param>
    /// <returns>The modifier value</returns>
    public static int CalculateModifier(int attributeScore)
    {
        return (attributeScore - 10) / 2;
    }
    
    /// <summary>
    /// Applies racial bonuses to base attributes based on actual game rules
    /// </summary>
    /// <param name="baseAttributes">The starting attribute scores</param>
    /// <param name="race">The character's race</param>
    /// <param name="humanChosenAttribute">For Humans, the attribute they choose to boost (optional)</param>
    /// <returns>Modified attributes with racial bonuses applied</returns>
    public static Dictionary<AttributeType, int> ApplyRacialBonuses(
        Dictionary<AttributeType, int> baseAttributes, 
        CharacterRace race,
        AttributeType? humanChosenAttribute = null)
    {
        var modifiedAttributes = new Dictionary<AttributeType, int>(baseAttributes);
        
        // Apply racial bonuses based on actual game rules
        switch (race)
        {
            case CharacterRace.Human:
                // Humans get +1 to any one attribute of their choice
                if (humanChosenAttribute.HasValue)
                {
                    modifiedAttributes[humanChosenAttribute.Value] += 1;
                }
                break;
                
            case CharacterRace.Duskborn:
                // Agile shadow-dwellers: +2 Agility, -1 Might
                modifiedAttributes[AttributeType.Agility] += 2;
                modifiedAttributes[AttributeType.Might] -= 1;
                break;
                
            case CharacterRace.Ironforged:
                // Resilient constructs: +2 Vitality, -1 Agility
                modifiedAttributes[AttributeType.Vitality] += 2;
                modifiedAttributes[AttributeType.Agility] -= 1;
                break;
                
            case CharacterRace.Wildkin:
                // Nature-attuned shapeshifters: +2 Wits, -1 Presence
                modifiedAttributes[AttributeType.Wits] += 2;
                modifiedAttributes[AttributeType.Presence] -= 1;
                break;
                
            case CharacterRace.Emberfolk:
                // Charismatic fire-touched: +2 Presence, -1 Wits
                modifiedAttributes[AttributeType.Presence] += 2;
                modifiedAttributes[AttributeType.Wits] -= 1;
                break;
                
            case CharacterRace.Stoneborn:
                // Mountain-dwelling powerhouses: +1 Might, +1 Vitality, -2 Agility
                modifiedAttributes[AttributeType.Might] += 1;
                modifiedAttributes[AttributeType.Vitality] += 1;
                modifiedAttributes[AttributeType.Agility] -= 2;
                break;
        }
        
        return modifiedAttributes;
    }
    
    /// <summary>
    /// Validates that attributes match the Standard Array (15, 14, 13, 12, 10)
    /// </summary>
    /// <param name="attributes">The proposed attribute scores before racial modifiers</param>
    /// <returns>True if attributes match Standard Array distribution</returns>
    public static bool ValidateStandardArray(Dictionary<AttributeType, int> attributes)
    {
        // Standard Array: 15, 14, 13, 12, 10
        var expectedScores = new List<int> { 15, 14, 13, 12, 10 };
        var actualScores = attributes.Values.OrderByDescending(x => x).ToList();
        
        if (actualScores.Count != expectedScores.Count)
            return false;
            
        for (var i = 0; i < expectedScores.Count; i++)
        {
            if (actualScores[i] != expectedScores[i])
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validates rolled attributes (4d6 drop lowest method)
    /// Each score should be between 3-18
    /// </summary>
    /// <param name="attributes">The proposed attribute scores</param>
    /// <returns>True if all scores are within valid range</returns>
    public static bool ValidateRolledAttributes(Dictionary<AttributeType, int> attributes)
    {
        // Each attribute must be between 3 and 18
        foreach (var score in attributes.Values)
        {
            if (score < 3 || score > 18)
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validates attribute point allocation for character creation
    /// Kept for backward compatibility - use ValidateStandardArray or ValidateRolledAttributes instead
    /// </summary>
    /// <param name="attributes">The proposed attribute scores</param>
    /// <returns>True if valid, false otherwise</returns>
    [Obsolete("Use ValidateStandardArray or ValidateRolledAttributes instead")]
    public static bool ValidatePointBuy(Dictionary<AttributeType, int> attributes)
    {
        // Each attribute must be between 8 and 15 before racial modifiers
        foreach (var score in attributes.Values)
        {
            if (score < 8 || score > 15)
                return false;
        }
        
        // Calculate total cost using point-buy costs
        var totalCost = 0;
        foreach (var score in attributes.Values)
        {
            totalCost += GetPointBuyCost(score);
        }
        
        return totalCost == 27;
    }
    
    /// <summary>
    /// Gets the point-buy cost for a given attribute score
    /// </summary>
    private static int GetPointBuyCost(int score)
    {
        return score switch
        {
            8 => 0,
            9 => 1,
            10 => 2,
            11 => 3,
            12 => 4,
            13 => 5,
            14 => 7,
            15 => 9,
            _ => 0
        };
    }
    
    /// <summary>
    /// Calculates the total attribute modifier for a character
    /// Used for determining overall power level
    /// </summary>
    public static int CalculateTotalModifiers(Dictionary<AttributeType, int> attributes)
    {
        return attributes.Values.Sum(CalculateModifier);
    }
}
