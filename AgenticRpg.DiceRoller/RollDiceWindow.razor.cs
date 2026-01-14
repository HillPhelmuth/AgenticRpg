using System.Globalization;
using System.Reflection;
using System.Text.Json;
using AgenticRpg.DiceRoller.Components;
using AgenticRpg.DiceRoller.Models;
using Markdig;
using Microsoft.AspNetCore.Components;


namespace AgenticRpg.DiceRoller;

public partial class RollDiceWindow
{
    [Parameter]
    public Guid? ModalId { get; set; }
    
    [Parameter]
    public DiceRollComponentType ComponentType { get; set; }
    
    [Parameter]
    public RollDiceParameters? Parameters { get; set; }
    
    [Parameter]
    public RollDiceWindowOptions Options { get; set; } = new();
    
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
    
    [Inject]
    private IRollDiceService RollDiceService { get; set; } = default!;

    private bool IsOpen { get; set; } = true;
    private Type? _componentType;
    private RollDiceParameters? _normalizedParameters;

    protected override void OnParametersSet()
    {
        _componentType = ComponentType == DiceRollComponentType.DiceRoller 
            ? typeof(Components.DiceRoller) 
            : typeof(DieRoller);
        _normalizedParameters = NormalizeParameters(_componentType, Parameters);
        
        // Add ModalId to parameters so the component can close itself
        if (_normalizedParameters != null && ModalId.HasValue)
        {
            _normalizedParameters["ModalId"] = ModalId.Value;
        }
    }

    private void CloseSelf()
    {
        if (ModalId.HasValue)
        {
            RollDiceService.CloseSelf(ModalId.Value);
        }
    }
    
    private string AsHtml(string? text)
    {
        if (text == null) return "";
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var result = Markdown.ToHtml(text, pipeline);
        return result;

    }
    
    private void OutClick()
    {
        if (Options.CloseOnOuterClick)
        {
            CloseSelf();
        }
    }

    private void Close()
    {
        CloseSelf();
    }

    private static RollDiceParameters NormalizeParameters(Type? componentType, RollDiceParameters? parameters)
    {
        if (parameters == null || parameters.Count == 0 || componentType == null)
        {
            return parameters ?? new RollDiceParameters();
        }

        var normalized = new RollDiceParameters();
        foreach (var kvp in parameters)
        {
            var property = componentType.GetProperty(kvp.Key,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property == null)
            {
                continue; // # Reason: Passing unknown parameters to DynamicComponent crashes, so ignore anything the target lacks.
            }

            normalized[kvp.Key] = ConvertParameterValue(kvp.Value, property.PropertyType);
        }

        return normalized;
    }

    private static object? ConvertParameterValue(object? value, Type targetType)
    {
        if (value == null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is JsonElement element)
        {
            try
            {
                return JsonSerializer.Deserialize(element.GetRawText(), targetType);
            }
            catch
            {
                // Fall through to other conversion strategies
            }
        }

        if (targetType.IsEnum)
        {
            if (value is string enumString && Enum.TryParse(targetType, enumString, true, out var enumResult))
            {
                return enumResult;
            }

            if (value is IConvertible convertibleValue)
            {
                try
                {
                    var numericValue = Convert.ChangeType(convertibleValue, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture);
                    return Enum.ToObject(targetType, numericValue!);
                }
                catch
                {
                    // ignore and fall through
                }
            }
        }

        if (value is IConvertible convertible && typeof(IConvertible).IsAssignableFrom(targetType))
        {
            try
            {
                return Convert.ChangeType(convertible, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                // ignore and fall through
            }
        }

        return value;
    }
}
