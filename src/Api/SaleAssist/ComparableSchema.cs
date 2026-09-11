using System.Text.Json;

namespace Api.SaleAssist;

public static class ComparableSchema
{
    // Sent as text.format.schema with strict:true, so every property must stay in
    // required and additionalProperties must stay false.
    public const string Json =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["comparables"],
          "properties": {
            "comparables": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["title", "price", "url"],
                "properties": {
                  "title": { "type": "string" },
                  "price": { "type": "integer" },
                  "url": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    public static JsonElement Element { get; } = JsonDocument.Parse(Json).RootElement;
}
