using System.Text.Json;
using System.Text.Json.Serialization;

public class TolerantProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
