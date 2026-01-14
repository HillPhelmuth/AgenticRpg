using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRpg.DiceRoller.Models;

namespace AgenticRpg.DiceRoller;

public class DieRollerTools(IRollDiceService modalService)
{
    //[Description("Rolls a die with the specified number of sides.")]
    //public async Task<int> RollDie([Description("Current session or campaign Id")] string sessionId,
    //    [Description("Tell the player the purpose of the roll in one sentance or less")] string reasonForRolling,
    //    [Description("Type of the die based on number of sides")] DieType dieType,
    //    [Description("Value to add (or subtract, if negative) to the roll result")] int modifier = 0)
    //{
    //    var windowOptions = new RollDiceWindowOptions() { Title = $"Roll 1{dieType} for {reasonForRolling}", Location = Location.Center, Style = "width:max-content;min-width:50vw;height:max-content" };
    //    var parameters = new RollDiceParameters() { ["DieType"] = dieType, ["NumberOfRolls"] = 1 };
    //    var result = await modalService.RequestDiceRoll(sessionId, parameters, windowOptions);
    //    var returnItem = result?.Results?.Total;
    //    return returnItem.GetValueOrDefault() + modifier;
    //}
    [Description("Prompts the player to roll a die with the specified number of sides a specified number of times. (ex: 3D6 is D6 die rolled 3 times)")]
    public async Task<string> RollPlayerDice([Description("Current session or campaign Id")]string sessionId, [Description("Tell the player the purpose of the roll in one sentance or less")] string reasonForRolling, [Description("Type of the die based on number of sides")] DieType dieType, [Description("Number of dice of the indicated `dieType`")] int numberOfDice, [Description("Value to add (or subtract, if negative) to the roll result")] int modifier = 0,[Description("Ignore the lowest die roll")] bool dropLowest = false)
    {
        var windowOptions = new RollDiceWindowOptions() { Title = $"Roll {numberOfDice}{dieType} for {reasonForRolling}", Location = Location.Center, Style = "width:max-content;min-width:40vw;height:max-content" };
        var parameters = new RollDiceParameters() { ["DieType"] = dieType, ["NumberOfRolls"] = numberOfDice, ["DropLowest"] = dropLowest };
        var result = await modalService.RequestDiceRoll(sessionId, parameters, windowOptions);
       
        return JsonSerializer.Serialize(result);
        
    }

    [
     Description(
         "Automatically roll a die with the specified number of sides a specified number of times. (ex: 3D6 is D6 die rolled 3 times) for a monster or non-player character (NPC)")]
    public async Task<string> RollNonPlayerCharacterDice(
        [Description("Current session or campaign Id")] string sessionId,
        [Description("Tell the player the purpose of the roll in one sentance or less")] string reasonForRolling,
        [Description("Type of the die based on number of sides")] DieType dieType,
        [Description("Number of roles of the indicated die")] int numberOfRolls,
        [Description("Value to add (or subtract, if negative) to the roll result")] int modifier = 0)
    {
        var windowOptions = new RollDiceWindowOptions() { Title = $"Roll {numberOfRolls}{dieType} for {reasonForRolling}", Location = Location.TopRight, Style = "width:max-content;min-width:30vw;height:max-content" };
        var parameters = new RollDiceParameters() { ["DieType"] = dieType, ["NumberOfRolls"] = numberOfRolls, ["IsManual"] = false };
        var result = await modalService.RequestDiceRoll(sessionId, parameters, windowOptions);
       
        return JsonSerializer.Serialize(result);
        
    }
    
}
//[TypeConverter(typeof(GenericTypeConverter<DieRollResults>))]
public class DieRollResults(List<int> rollResults, int total)
{
    
    [JsonPropertyName("rollResults")]
    public List<int> RollResults { get; set; } = rollResults;
    [JsonPropertyName("total")]
    public int Total { get; set; } = total;

}
public class GenericTypeConverter<T> : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => true;

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        // TEMP Console.WriteLine($"Converting {value} to {typeof(T)}");
        return JsonSerializer.Deserialize<T>(value.ToString());
    }
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        // TEMP Console.WriteLine($"Converting {typeof(T)} to {value}");
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }
}