namespace AgenticRpg.Core.Helpers;

public static class EnumHelpers
{
    public static string GetDescription(this Enum value)
    {
        var type = value.GetType();
        var name = Enum.GetName(type, value);
        if (name == null)
        {
            return null;
        }
        var field = type.GetField(name);
        var attr = Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.DescriptionAttribute)) as System.ComponentModel.DescriptionAttribute;
        return attr?.Description;
    }
   
}