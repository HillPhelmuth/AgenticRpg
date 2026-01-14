using System.Text.Json;

namespace AgenticRpg.DiceRoller.Models;

public static class Helpers
{
    public static bool TryExractArrayFromJson(string jsonString, out List<string> value)
    {
        value = [];
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) 
                return false;
            var jsonProperty = root.EnumerateObject()
                .FirstOrDefault(property => property.Value.ValueKind == JsonValueKind.Array);
            var firstArray = jsonProperty.Value;
            value = ConvertToArrayOfDynamic(firstArray);
            return true;
        }
        catch (Exception ex)
        {
            // TEMP Console.WriteLine("Not Valid Json? " + ex.Message);
            return false;
        }

            
    }
    private static List<string> ConvertToArrayOfDynamic(JsonElement array)
    {
        return array.EnumerateArray().Select(element => element.GetRawText()).ToList();

    }
    public static int RollDie(this DieType dieType, int modifier = 0)
    {
        var random = new Random();
        var result = dieType switch
        {
            DieType.D4 => random.Next(1, 5),
            DieType.D6 => random.Next(1, 7),
            DieType.D8 => random.Next(1, 9),
            DieType.D10 => random.Next(1, 11),
            DieType.D20 => random.Next(1, 21),
            _ => throw new ArgumentOutOfRangeException(nameof(dieType), "Unsupported die type")
        };
        return result + modifier;
    }
}