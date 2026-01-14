namespace AgenticRpg.DiceRoller.Models;

public class RollDiceParameters : Dictionary<string, object?>
{
    public T? Get<T>(string parameterName)
    {
        if (!ContainsKey(parameterName))
            throw new KeyNotFoundException($"{parameterName} does not exist in modal parameters");

        return (T?)this[parameterName];
    }
    public T? Get<T>(string parameterName, T defaultValue)
    {
        if (TryGetValue(parameterName, out var parameterValue))
            return (T?)parameterValue;
        parameterValue = defaultValue;
        this[parameterName] = parameterValue;
        return (T?)parameterValue;
    }
    public void Set<T>(string parameterName, T parameterValue)
    {
        this[parameterName] = parameterValue;
    }
    public DieType DieType
    {
        get => Get("DieType", DieType.D20);
        set => Set("DieType", value);
    }
    public int NumberOfRolls 
    {
        get => Get("NumberOfRolls", 1);
        set => Set("NumberOfRolls", value);
    }
    public bool IsManual 
    {
        get => Get("IsManual", true);
        set => Set("IsManual", value);
    }
}
