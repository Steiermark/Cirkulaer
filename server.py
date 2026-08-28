import base64
import json
import os
import re
import urllib.error
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

from decision_engine import build_recommendation


PORT = int(os.environ.get("PORT", "4173"))
MODEL = os.environ.get("OPENAI_MODEL", "gpt-5")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")


ASSESSMENT_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "properties": {
        "object_name": {"type": "string"},
        "category": {"type": "string"},
        "subcategory": {"type": ["string", "null"]},
        "brand": {"type": ["string", "null"]},
        "model": {"type": ["string", "null"]},
        "materials": {"type": "array", "items": {"type": "string"}},
        "visible_damage": {"type": "array", "items": {"type": "string"}},
        "condition_estimate": {
            "type": "string",
            "enum": ["new", "good", "worn", "damaged", "unknown"],
        },
        "confidence": {"type": "number", "minimum": 0, "maximum": 1},
        "waste_category": {"type": "string"},
        "uncertainty_notes": {"type": "array", "items": {"type": "string"}},
    },
    "required": [
        "object_name",
        "category",
        "subcategory",
        "brand",
        "model",
        "materials",
        "visible_damage",
        "condition_estimate",
        "confidence",
        "waste_category",
        "uncertainty_notes",
    ],
}


class Handler(SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_POST(self):
        if self.path == "/api/recommend":
            self.handle_recommend()
            return

        if self.path != "/api/analyze":
            self.send_json({"error": "Not found"}, status=404)
            return

        self.handle_analyze()

    def handle_recommend(self):
        try:
            payload = self.read_json_body()
            assessment = payload.get("assessment")
            answers = payload.get("answers")
            if not isinstance(assessment, dict) or not isinstance(answers, dict):
                raise ValueError("Assessment og svar skal sendes som objekter.")

            self.send_json({"recommendation": build_recommendation(assessment, answers)})
        except ValueError as exc:
            self.send_json({"error": str(exc)}, status=400)
        except Exception as exc:
            self.send_json({"error": str(exc)}, status=500)

    def handle_analyze(self):
        if not OPENAI_API_KEY:
            self.send_json(
                {
                    "error": (
                        "OPENAI_API_KEY mangler. Start serveren med en OpenAI API-nøgle "
                        "for at bruge rigtig billedgenkendelse."
                    )
                },
                status=503,
            )
            return

        try:
            payload = self.read_json_body()
            image_data_url = payload.get("imageDataUrl", "")
            if not image_data_url.startswith("data:image/"):
                raise ValueError("Upload et gyldigt billede.")

            assessment = analyze_with_openai(image_data_url)
            self.send_json({"assessment": assessment})
        except ValueError as exc:
            self.send_json({"error": str(exc)}, status=400)
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            self.send_json(
                {"error": "OpenAI-kaldet fejlede.", "detail": detail}, status=502
            )
        except Exception as exc:
            self.send_json({"error": str(exc)}, status=500)

    def read_json_body(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length > 9_000_000:
            raise ValueError("Billedet er for stort til denne prototype.")
        raw = self.rfile.read(length)
        return json.loads(raw.decode("utf-8"))

    def send_json(self, payload, status=200):
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def analyze_with_openai(image_data_url):
    prompt = (
        "Du analyserer et foto af en fysisk genstand for en dansk cirkulær "
        "økonomi-assistent. "
        "Gæt ikke på mærke eller model, hvis det ikke tydeligt fremgår. "
        "Kommunale affaldsregler må ikke opfindes. Brug kun en generel dansk "
        "affaldsfraktion, og skriv usikkerheder eksplicit. "
        "Skriv alle tekstfelter på dansk."
    )

    body = {
        "model": MODEL,
        "input": [
            {
                "role": "user",
                "content": [
                    {"type": "input_text", "text": prompt},
                    {"type": "input_image", "image_url": image_data_url},
                ],
            }
        ],
        "text": {
            "format": {
                "type": "json_schema",
                "name": "circular_object_assessment",
                "strict": True,
                "schema": ASSESSMENT_SCHEMA,
            }
        },
    }

    request = urllib.request.Request(
        "https://api.openai.com/v1/responses",
        data=json.dumps(body).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {OPENAI_API_KEY}",
            "Content-Type": "application/json",
        },
        method="POST",
    )

    with urllib.request.urlopen(request, timeout=60) as response:
        result = json.loads(response.read().decode("utf-8"))

    text = extract_response_text(result)
    assessment = parse_json_object(text)
    return normalize_assessment(assessment)


def extract_response_text(result):
    texts = []
    for item in result.get("output", []):
        for content in item.get("content", []):
            if content.get("type") == "output_text":
                texts.append(content.get("text", ""))
    if not texts:
        raise ValueError("AI returnerede ikke tekst.")
    return "\n".join(texts)


def parse_json_object(text):
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", text, re.DOTALL)
        if not match:
            raise ValueError("AI returnerede ikke gyldig JSON.")
        return json.loads(match.group(0))


def normalize_assessment(data):
    confidence = data.get("confidence", 0.0)
    if not isinstance(confidence, (int, float)):
        confidence = 0.0

    materials = data.get("materials", [])
    visible_damage = data.get("visible_damage", [])
    uncertainty_notes = data.get("uncertainty_notes", [])

    return {
        "object_name": str(data.get("object_name") or "Ukendt genstand"),
        "category": str(data.get("category") or "Ukendt kategori"),
        "subcategory": data.get("subcategory"),
        "brand": data.get("brand"),
        "model": data.get("model"),
        "materials": materials if isinstance(materials, list) else [],
        "visible_damage": visible_damage if isinstance(visible_damage, list) else [],
        "condition_estimate": data.get("condition_estimate") or "unknown",
        "confidence": max(0.0, min(1.0, float(confidence))),
        "waste_category": str(data.get("waste_category") or "Ukendt fraktion"),
        "uncertainty_notes": (
            uncertainty_notes
            if isinstance(uncertainty_notes, list) and uncertainty_notes
            else ["AI-vurderingen indeholder usikkerhed og bør bekræftes af brugeren."]
        ),
    }


def main():
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"Serving Cirkulær assistent on http://127.0.0.1:{PORT}")
    server.serve_forever()


if __name__ == "__main__":
    main()
