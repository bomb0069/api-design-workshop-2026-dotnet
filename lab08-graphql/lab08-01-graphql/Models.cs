// GraphQL object types (mirrors the type definitions in schema.go) and the
// JSON shape used by the REST comparison endpoint.
using System.Text.Json.Serialization;

public record Product
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("price")] public double Price { get; init; }
    [JsonPropertyName("category")] public string Category { get; init; } = "";
}

public record Category
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("count")] public int Count { get; init; }
}
