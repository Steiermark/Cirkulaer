using System.Text.Json;

namespace Api.Ai;

public static class AssessmentSchema
{
    // Copied verbatim from legacy/server.py:22-59, plus search_terms (2026-09-11). Sent to
    // OpenAI as text.format.schema with strict:true, so every property must stay in
    // required and additionalProperties must stay false.
    public const string Json =
        """
        {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                        "object_name": {
                                "type": "string"
                        },
                        "category": {
                                "type": "string"
                        },
                        "category_id": {
                                "type": "string",
                                "enum": [
                                        "electronics",
                                        "furniture",
                                        "bicycle",
                                        "textile",
                                        "hazardous",
                                        "other"
                                ]
                        },
                        "subcategory": {
                                "type": [
                                        "string",
                                        "null"
                                ]
                        },
                        "brand": {
                                "type": [
                                        "string",
                                        "null"
                                ]
                        },
                        "model": {
                                "type": [
                                        "string",
                                        "null"
                                ]
                        },
                        "materials": {
                                "type": "array",
                                "items": {
                                        "type": "string"
                                }
                        },
                        "visible_damage": {
                                "type": "array",
                                "items": {
                                        "type": "string"
                                }
                        },
                        "condition_estimate": {
                                "type": "string",
                                "enum": [
                                        "new",
                                        "good",
                                        "worn",
                                        "damaged",
                                        "unknown"
                                ]
                        },
                        "confidence": {
                                "type": "number",
                                "minimum": 0,
                                "maximum": 1
                        },
                        "waste_category": {
                                "type": "string"
                        },
                        "uncertainty_notes": {
                                "type": "array",
                                "items": {
                                        "type": "string"
                                }
                        },
                        "search_terms": {
                                "type": "array",
                                "items": {
                                        "type": "string"
                                }
                        }
                },
                "required": [
                        "object_name",
                        "category",
                        "category_id",
                        "subcategory",
                        "brand",
                        "model",
                        "materials",
                        "visible_damage",
                        "condition_estimate",
                        "confidence",
                        "waste_category",
                        "uncertainty_notes",
                        "search_terms"
                ]
        }
        """;

    public static JsonElement Element { get; } = JsonDocument.Parse(Json).RootElement;
}
