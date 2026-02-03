using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AgenticRpg.Core.Helpers;

public static class ReflectionHelpers
{
    private static readonly ConcurrentDictionary<Type, (string Name, string Type)[]> _cache = new();

    public static IReadOnlyList<(string Name, string Type)> GetPublicPropertyNamesAndTypes(object instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (instance is JsonElement jsonElement)
        {
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var jsProps = new List<(string Name, string Type)>();
                    foreach (var property in jsonElement.EnumerateObject())
                    {
                        jsProps.Add((property.Name, property.Value.ValueKind.ToString()));
                    }
                    return jsProps;
                }
                case JsonValueKind.Array:
                {
                    var jsProps = new List<(string Name, string Type)>();
                    int index = 0;
                    foreach (var item in jsonElement.EnumerateArray())
                    {
                        jsProps.Add(($"[{index}]", item.ValueKind.ToString()));
                        index++;
                    }
                    return jsProps;
                }
                case JsonValueKind.String:
                    return new List<(string Name, string Type)> { ("Value", $"{jsonElement.GetString()}") };
            }
        }

        var type = instance.GetType();

        // Cache per runtime type (fast path after first hit)
        var props = _cache.GetOrAdd(type, static t =>
        {
            // Only public instance properties with a public getter (indexers excluded)
            return t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetMethod is not null && p.GetMethod.IsPublic)
                .OrderBy(x => x.Name)
                .Select(p => (p.Name, p.PropertyType.Name))
                .ToArray();
        });

        return props;
    }
}