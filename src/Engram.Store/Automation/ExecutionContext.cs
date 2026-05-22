using System;
using System.Collections.Concurrent;

namespace Engram.Store.Automation;

public class ExecutionContext
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    
    public ConcurrentDictionary<string, object> Variables { get; } = new();

    public void SetVariable(string key, object value)
    {
        Variables[key] = value;
    }

    public T? GetVariable<T>(string key)
    {
        if (Variables.TryGetValue(key, out var val) && val is T typedVal)
        {
            return typedVal;
        }
        return default;
    }

    public string SerializeState()
    {
        var serializableDict = new Dictionary<string, object>();
        foreach (var kvp in Variables)
        {
            if (kvp.Value is string or int or double or float or decimal or bool or DateTime or DateTimeOffset or Guid)
            {
                serializableDict[kvp.Key] = kvp.Value;
            }
        }
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            SessionId,
            Variables = serializableDict
        });
    }

    public void DeserializeState(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.TryGetProperty("Variables", out var varsProp) && varsProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in varsProp.EnumerateObject())
            {
                switch (prop.Value.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.String:
                        if (prop.Value.TryGetDateTimeOffset(out var dto))
                            Variables[prop.Name] = dto;
                        else if (prop.Value.TryGetGuid(out var guid))
                            Variables[prop.Name] = guid;
                        else
                            Variables[prop.Name] = prop.Value.GetString()!;
                        break;
                    case System.Text.Json.JsonValueKind.Number:
                        if (prop.Value.TryGetInt32(out var i))
                            Variables[prop.Name] = i;
                        else if (prop.Value.TryGetDouble(out var d))
                            Variables[prop.Name] = d;
                        break;
                    case System.Text.Json.JsonValueKind.True:
                        Variables[prop.Name] = true;
                        break;
                    case System.Text.Json.JsonValueKind.False:
                        Variables[prop.Name] = false;
                        break;
                }
            }
        }
    }
}
