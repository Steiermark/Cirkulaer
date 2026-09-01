import base64
import html
import json
import os
import re
import socket
import urllib.error
import urllib.parse
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

from decision_engine import build_recommendation
from producer_programs import find_program_candidates



HOST = os.environ.get("HOST", "0.0.0.0")
PORT = int(os.environ.get("PORT", "4173"))
MODEL = os.environ.get("OPENAI_MODEL", "gpt-5")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")

ASSESSMENT_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "properties": {
        "object_name": {"type": "string"},
        "category": {"type": "string"},
        "category_id": {
            "type": "string",
            "enum": ["electronics", "furniture", "bicycle", "textile", "hazardous", "other"],
        },
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

        if self.path == "/api/sale-assist":
            self.handle_sale_assist()
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
    def handle_sale_assist(self):
        try:
            payload = self.read_json_body()
            assessment = payload.get("assessment")
            answers = payload.get("answers") or {}
            recommendation = payload.get("recommendation") or {}
            if not isinstance(assessment, dict):
                raise ValueError("Assessment skal sendes som objekt.")
            if not isinstance(answers, dict) or not isinstance(recommendation, dict):
                raise ValueError("Svar og anbefaling skal sendes som objekter.")

            self.send_json({"sale": build_sale_assist(assessment, answers, recommendation)})
        except ValueError as exc:
            self.send_json({"error": str(exc)}, status=400)
        except Exception as exc:
            self.send_json({"error": str(exc)}, status=500)

    def handle_analyze(self):
        try:
            payload = self.read_json_body()
            images = payload.get("images")
            if isinstance(images, list):
                image_items = images
            else:
                image_items = [
                    {
                        "filename": payload.get("filename", ""),
                        "imageDataUrl": payload.get("imageDataUrl", ""),
                    }
                ]

            image_items = image_items[:4]
            image_data_urls = [item.get("imageDataUrl", "") for item in image_items]
            filenames = [item.get("filename", "") for item in image_items]
            if not image_data_urls or any(
                not image_data_url.startswith("data:image/")
                for image_data_url in image_data_urls
            ):
                raise ValueError("Upload 1-4 gyldige billeder.")

            if not OPENAI_API_KEY:
                self.send_json(
                    {
                        "assessment": build_test_assessment(" ".join(filenames)),
                        "mode": "test",
                        "message": (
                            "Testversion: billederne er modtaget og vist, men objektet "
                            "er vurderet med lokal testlogik, fordi OPENAI_API_KEY mangler."
                        ),
                    }
                )
                return

            assessment = analyze_with_openai(image_data_urls)
            self.send_json({"assessment": assessment, "mode": "ai"})
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
        if length > 24_000_000:
            raise ValueError("Billederne er for store til denne prototype.")
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


def build_test_assessment(filename):
    normalized = filename.lower()
    hints = [
        {
            "keywords": ["billy"],
            "object_name": "BILLY-reol",
            "category": "Møbler og indbo",
            "subcategory": "Bogreol",
            "brand": "IKEA",
            "model": "BILLY",
            "materials": ["spånplade", "træfiberplade"],
            "waste_category": "Storskrald eller genbrugsplads",
            "confidence": 0.96,
        },
        {
            "keywords": ["reol", "stol", "bord", "ikea", "skab", "moebel", "møbel"],
            "object_name": "Møbel",
            "category": "Møbler og indbo",
            "subcategory": "Møbel",
            "brand": "IKEA" if "ikea" in normalized or "billy" in normalized else None,
            "materials": ["træ", "metal"],
            "waste_category": "Storskrald eller genbrugsplads",
            "confidence": 0.42,
        },
        {
            "keywords": ["boremaskine", "drill", "bosch", "makita", "dewalt"],
            "object_name": "Akkuboremaskine",
            "category": "Elektronik og værktøj",
            "subcategory": "Elværktøj",
            "brand": None,
            "materials": ["plast", "metal", "batteri"],
            "waste_category": "Småt elektronik",
            "confidence": 0.44,
        },
        {
            "keywords": ["telefon", "iphone", "samsung", "mobil"],
            "object_name": "Mobiltelefon",
            "category": "Elektronik",
            "subcategory": "Telefon",
            "brand": None,
            "materials": ["glas", "metal", "batteri"],
            "waste_category": "Småt elektronik",
            "confidence": 0.44,
        },
        {
            "keywords": ["jakke", "bukser", "troeje", "trøje", "sko", "tekstil"],
            "object_name": "Tekstil eller tøj",
            "category": "Tekstiler",
            "subcategory": "Tøj",
            "brand": None,
            "materials": ["tekstil"],
            "waste_category": "Tekstilaffald",
            "confidence": 0.4,
        },        {
            "keywords": ["cykel", "bike", "bicycle", "mountainbike", "racercykel", "elcykel"],
            "object_name": "Cykel",
            "category": "Cykel og fritid",
            "subcategory": "Cykel",
            "brand": None,
            "materials": ["metal", "gummi", "plast"],
            "waste_category": "Jern og metal eller storskrald",
            "confidence": 0.46,
        },
    ]

    match = next(
        (item for item in hints if any(keyword in normalized for keyword in item["keywords"])),
        None,
    )
    if not match:
        match = {
            "object_name": "Ukendt testgenstand",
            "category": "Blandet genstand",
            "subcategory": None,
            "brand": None,
            "materials": [],
            "waste_category": "Afhænger af materiale og lokal ordning",
            "confidence": 0.25,
        }

    assessment = {
        "object_name": match["object_name"],
        "category": match["category"],
        "category_id": canonical_category_id(match["category"]),
        "subcategory": match["subcategory"],
        "brand": match["brand"],
        "model": match.get("model"),
        "materials": match["materials"],
        "visible_damage": [],
        "condition_estimate": "unknown",
        "confidence": match["confidence"],
        "waste_category": match["waste_category"],
        "uncertainty_notes": [
            "Testversion: billedet er uploadet, men der er ikke brugt rigtig AI-genkendelse endnu.",
            "Genstanden er kun groft foreslået ud fra filnavn, så bekræft oplysningerne i spørgsmålene.",
        ],
        "analysis_mode": "test",
    }
    assessment["producer_program_candidates"] = find_program_candidates(assessment)
    return assessment

def analyze_with_openai(image_data_urls):
    prompt = (
        "Du analyserer 1-4 fotos af den samme fysiske genstand for en dansk cirkulær "
        "økonomi-assistent. Brug alle vinkler samlet. Hvis et foto viser en mærkeplade, "
        "etiket eller original mærkning, skal du bruge den til at identificere producent, mærke og model. "
        "Gæt ikke på mærke eller model, hvis det ikke tydeligt fremgår. "
        "Kommunale affaldsregler må ikke opfindes. Brug kun en generel dansk "
        "affaldsfraktion, og skriv usikkerheder eksplicit. "
        "Vælg category_id fra den faste liste i skemaet. "
        "Skriv alle tekstfelter på dansk."
    )

    body = {
        "model": MODEL,
        "input": [
            {
                "role": "user",
                "content": [
                    {"type": "input_text", "text": prompt},
                    *[
                        {"type": "input_image", "image_url": image_data_url}
                        for image_data_url in image_data_urls[:4]
                    ],
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

    assessment = {
        "object_name": str(data.get("object_name") or "Ukendt genstand"),
        "category": str(data.get("category") or "Ukendt kategori"),
        "category_id": data.get("category_id") or canonical_category_id(data.get("category")),
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
    assessment["producer_program_candidates"] = find_program_candidates(assessment)
    return assessment


def canonical_category_id(category):
    value = str(category or "").lower()
    if "elektronik" in value or "værktøj" in value or "vaerktoej" in value:
        return "electronics"
    if "møbl" in value or "moebl" in value or "mobl" in value:
        return "furniture"
    if "cykel" in value or "bike" in value:
        return "bicycle"
    if "tekstil" in value or "tøj" in value or "toej" in value:
        return "textile"
    if "farligt" in value or "kemi" in value:
        return "hazardous"
    return "other"

def build_sale_assist(assessment, answers, recommendation):
    query = build_sale_query(assessment, answers)
    marketplace_url = build_marketplace_search_url(query)
    reshopper_relevant = is_reshopper_relevant(assessment)
    search = search_price_signals(query, reshopper_relevant)
    estimate = estimate_sale_price(assessment, answers, search["prices"])
    object_name = build_sale_object_name(assessment, answers)

    return {
        "object_name": object_name,
        "details": build_sale_details(assessment, answers),
        "price": estimate["label"],
        "price_note": estimate["note"],
        "search_note": search["note"],
        "search_url": search["url"],
        "marketplace_search_url": marketplace_url,
        "reshopper_relevant": reshopper_relevant,
        "reshopper_url": build_reshopper_url(),
        "reshopper_note": build_reshopper_note(assessment),
        "ad_text": build_ad_text(object_name, assessment, answers, estimate),
        "marketplace_note": (
            "Direkte oprettelse på Facebook Marketplace kræver officiel adgang. "
            "Facebook Marketplace bruges her som manuel priskontrol via søgelink. "
            "I denne prototype kan annoncen kopieres og Marketplace åbnes manuelt."
        ),
        "signals": search["signals"],
        "comparables": search["comparables"],
        "price_confidence": search["confidence"],
    }


def build_sale_query(assessment, answers):
    parts = [
        producer_search_name(answers),
        answers.get("model_name"),
        assessment.get("brand"),
        assessment.get("model"),
        assessment.get("object_name"),
        assessment.get("subcategory"),
    ]
    clean_parts = []
    seen = set()
    for part in parts:
        value = normalize_search_terms(str(part or "").strip())
        key = value.lower()
        if value and key not in seen:
            seen.add(key)
            clean_parts.append(value)
    clean_parts = compact_redundant_parts(clean_parts)
    query = " ".join(clean_parts)
    return f"{query} brugt pris Danmark".strip()


def normalize_search_terms(value):
    replacements = {
        "mardone": "Madone",
        "Mardone": "Madone",
        "etep": "eTap",
        "Etep": "eTap",
        "ETEP": "eTap",
        "trek": "Trek",
        "slr": "SLR",
    }
    for old, new in replacements.items():
        value = value.replace(old, new)
    return value


def compact_redundant_parts(parts):
    compact = []
    lowered = [part.lower() for part in parts]
    for index, part in enumerate(parts):
        key = lowered[index]
        contained_by_longer = any(
            index != other_index
            and len(other_key) > len(key)
            and re.search(rf"(?<![a-z0-9]){re.escape(key)}(?![a-z0-9])", other_key)
            for other_index, other_key in enumerate(lowered)
        )
        if not contained_by_longer:
            compact.append(part)
    return compact

def producer_search_name(answers):
    producer_name = str(answers.get("producer_name") or "").strip()
    if producer_name:
        return producer_name

    producer = str(answers.get("producer") or "").strip()
    if producer == "ikea":
        return "IKEA"
    if producer and producer not in ("unknown", "other", "detected", "ved ikke", "anden"):
        return producer
    return ""

def build_marketplace_search_url(query):
    encoded_path = urllib.parse.quote(str(query or "").strip())
    return f"https://www.facebook.com/marketplace/search/?query={encoded_path}"


def build_reshopper_url():
    return "https://reshopper.com/da"


def is_reshopper_relevant(assessment):
    category = str(assessment.get("category") or "").lower()
    object_name = str(assessment.get("object_name") or "").lower()
    subcategory = str(assessment.get("subcategory") or "").lower()
    text = f"{category} {object_name} {subcategory}"
    relevant_terms = (
        "barn", "børn", "boern", "baby", "legetøj", "legetoej", "barnevogn",
        "klapvogn", "autostol", "børnetøj", "boernetoej", "ventetøj", "ventetoej",
        "tøj", "toej", "tekstil", "møbel", "moebel", "møbler", "moebler", "bolig",
    )
    excluded_terms = ("cykel", "elektronik", "værktøj", "vaerktoej", "batteri", "maling", "farligt")
    return any(term in text for term in relevant_terms) and not any(term in text for term in excluded_terms)


def build_reshopper_note(assessment):
    if is_reshopper_relevant(assessment):
        return "Reshopper vises, fordi genstanden ser ud til at passe til børn, mor eller bolig. Søg manuelt i appen med producent, model og genstandens navn."
    return "Reshopper er skjult, fordi platformen primært er relevant for børn, mor og bolig."

def build_sale_object_name(assessment, answers):
    parts = [
        producer_search_name(answers),
        answers.get("model_name"),
        assessment.get("brand"),
        assessment.get("model"),
        assessment.get("object_name"),
    ]
    seen = set()
    clean_parts = []
    for part in parts:
        value = normalize_search_terms(str(part or "").strip())
        key = value.lower()
        if value and key not in seen:
            seen.add(key)
            clean_parts.append(value)
    clean_parts = compact_redundant_parts(clean_parts)
    return " ".join(clean_parts) or "Genstand"


def build_sale_details(assessment, answers):
    details = []
    category = assessment.get("category")
    condition = answers.get("damage")
    works = answers.get("works")
    if category:
        details.append(str(category))
    if works == "yes":
        details.append("virker")
    elif works == "partly":
        details.append("virker delvist")
    elif works == "no":
        details.append("virker ikke")
    if condition == "no":
        details.append("ingen kendte skader")
    elif condition == "minor":
        details.append("mindre skader/slitage")
    elif condition == "major":
        details.append("store skader")
    return " · ".join(details)


def search_price_signals(query, include_reshopper=False):
    encoded = urllib.parse.quote_plus(query)
    url = f"https://duckduckgo.com/html/?q={encoded}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "Mozilla/5.0 CirkulaerPrototype/1.0"},
        method="GET",
    )

    try:
        with urllib.request.urlopen(request, timeout=12) as response:
            page = response.read().decode("utf-8", errors="replace")
    except Exception:
        return {
            "url": f"https://www.google.com/search?q={encoded}",
            "prices": [],
            "signals": [],
            "comparables": [],
            "confidence": "lav",
            "note": "Net-søgningen kunne ikke gennemføres fra prototypen. Linket åbner en manuel søgning efter lignende genstande.",
        }

    comparables = extract_comparables(page, query)
    signals = [item["title"] for item in comparables[:5]]
    prices = [item["price"] for item in comparables]
    confidence = "høj" if len(comparables) >= 5 else "middel" if len(comparables) >= 3 else "lav"
    extra_platforms = "Facebook Marketplace og Reshopper" if include_reshopper else "Facebook Marketplace"
    note = (
        f"Prisforslaget er baseret på en web-søgning efter: {query}. Brug også {extra_platforms} til at sammenligne relevante annoncer. "
        if prices
        else f"Der blev ikke fundet tydelige danske prisangivelser i web-søgningen. Brug web-linket og {extra_platforms} til manuel priskontrol. "
    )
    if prices:
        note += f"Der blev fundet {len(comparables)} prisfund med relevant titeltekst. "
    return {
        "url": url,
        "prices": prices,
        "signals": signals,
        "comparables": comparables[:8],
        "confidence": confidence,
        "note": note,
    }


def extract_comparables(page, query):
    title_matches = list(
        re.finditer(
            r'<a[^>]*class="[^"]*result__a[^"]*"[^>]*href="([^"]+)"[^>]*>(.*?)</a>',
            page,
            flags=re.DOTALL | re.IGNORECASE,
        )
    )
    query_tokens = comparable_query_tokens(query)
    comparables = []
    for index, match in enumerate(title_matches[:20]):
        segment_end = title_matches[index + 1].start() if index + 1 < len(title_matches) else min(len(page), match.end() + 2500)
        segment = page[match.start():segment_end]
        title = clean_html_text(match.group(2))
        segment_text = clean_html_text(segment)
        prices = extract_prices(segment_text)
        if not title or not prices:
            continue
        title_tokens = set(re.findall(r"[a-z0-9æøå]+", title.lower()))
        matched_tokens = [token for token in query_tokens if token in title_tokens]
        relevance = len(matched_tokens) / max(1, len(query_tokens))
        if relevance < 0.35:
            continue
        url = decode_search_result_url(html.unescape(match.group(1)))
        comparables.append(
            {
                "title": title,
                "price": prices[0],
                "relevance": round(relevance, 2),
                "url": url,
            }
        )
    return comparables


def comparable_query_tokens(query):
    stop_words = {
        "brugt", "pris", "danmark", "den", "det", "med", "og", "til",
        "moebler", "moebel", "indbo", "kategori",
    }
    normalized = normalize_search_terms(str(query or "")).lower()
    return [
        token
        for token in re.findall(r"[a-z0-9æøå]+", normalized)
        if (len(token) >= 3 or token.isdigit()) and token not in stop_words
    ]


def clean_html_text(value):
    return html.unescape(re.sub(r"\s+", " ", re.sub(r"<.*?>", " ", value))).strip()


def decode_search_result_url(url):
    parsed = urllib.parse.urlparse(url)
    target = urllib.parse.parse_qs(parsed.query).get("uddg", [])
    return target[0] if target else url


def extract_search_signals(page):
    titles = re.findall(r'class="result__a"[^>]*>(.*?)</a>', page, flags=re.DOTALL)
    clean = []
    for title in titles[:5]:
        text = re.sub(r"<.*?>", " ", title)
        text = html.unescape(re.sub(r"\s+", " ", text)).strip()
        if text:
            clean.append(text)
    return clean


def extract_prices(text):
    prices = []
    for match in re.findall(r"(?<!\d)(\d{2,6}(?:[\.,]\d{3})?)\s*(?:kr\.?|dkk|,-)", text, flags=re.IGNORECASE):
        value = int(re.sub(r"\D", "", match))
        if 25 <= value <= 100000:
            prices.append(value)

    for match in re.findall(r"(?<!\d)(\d{1,3})\s*(?:tusind|t\.kr\.?|k)\b", text, flags=re.IGNORECASE):
        value = int(match) * 1000
        if 1000 <= value <= 100000:
            prices.append(value)

    return sorted(prices[:30])


def estimate_sale_price(assessment, answers, prices):
    category = str(assessment.get("category") or "").lower()
    object_name = str(assessment.get("object_name") or "").lower()
    text = " ".join(
        str(value or "").lower()
        for value in [
            category,
            object_name,
            assessment.get("subcategory"),
            answers.get("producer_name"),
            answers.get("model_name"),
        ]
    )
    is_bicycle = "cykel" in text or "bike" in text

    if is_bicycle:
        return estimate_bicycle_price(answers, prices)

    filtered = trim_price_outliers(prices, minimum=50, maximum=100000)
    if filtered:
        midpoint = filtered[len(filtered) // 2]
        midpoint *= age_price_factor(answers.get("age"))
        midpoint *= damage_price_factor(answers)
        low = round_to_nearest_25(midpoint * 0.8)
        high = round_to_nearest_25(midpoint * 1.15)
        quick = round_to_nearest_25(midpoint * 0.7)
        return {
            "label": f"Sæt prisen til {round_to_nearest_50(midpoint)} kr.",
            "note": f"Pris sat ud fra medianen af lignende webfund. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr. Kontrollér aktive annoncer og stand før publicering.",
        }

    if "møbel" in category or "møbler" in category:
        low, high = 200, 900
    elif "elektronik" in category:
        low, high = 150, 700
    else:
        low, high = 100, 500

    if answers.get("damage") == "minor":
        low, high = round_to_nearest_25(low * 0.75), round_to_nearest_25(high * 0.75)
    if answers.get("damage") == "major" or answers.get("works") == "partly":
        low, high = round_to_nearest_25(low * 0.5), round_to_nearest_25(high * 0.55)

    low = round_to_nearest_25(low * age_price_factor(answers.get("age")))
    high = round_to_nearest_25(high * age_price_factor(answers.get("age")))
    return {
        "label": f"Sæt prisen til {round_to_nearest_50((low + high) / 2)} kr.",
        "note": f"Foreløbigt prototypeestimat, fordi der ikke blev fundet nok tydelige priser online. Realistisk spænd: {low}-{high} kr.",
    }


def estimate_bicycle_price(answers, prices):
    if is_premium_bicycle(answers):
        return estimate_premium_bicycle_price(answers, prices)

    filtered = trim_price_outliers(prices, minimum=450, maximum=15000)
    working = answers.get("works") == "yes"
    minor_or_better = answers.get("damage") in ("no", "minor", None, "unknown")
    complete = answers.get("accessories") in ("complete", "irrelevant", None)
    known_model = bool(str(producer_search_name(answers) or "").strip() or str(answers.get("model_name") or "").strip())

    if filtered:
        midpoint = filtered[len(filtered) // 2]
        midpoint *= age_price_factor(answers.get("age"))
        midpoint *= damage_price_factor(answers)
        low_factor, high_factor, quick_factor = 0.82, 1.28, 0.72
        if working and minor_or_better and complete:
            midpoint = max(midpoint, 1900 if known_model else 1500)
            low_factor, high_factor, quick_factor = 0.8, 1.35, 0.68
        low = round_to_nearest_50(midpoint * low_factor)
        high = round_to_nearest_50(midpoint * high_factor)
        quick = round_to_nearest_50(midpoint * quick_factor)
        return {
            "label": f"Sæt prisen til {round_to_nearest_50(midpoint)} kr.",
            "note": f"Pris sat ud fra medianen af lignende cykelpriser og justeret efter stand, komplethed og modeloplysninger. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr.; meget slidte cykler kan ligge lavere.",
        }

    if working and minor_or_better and complete:
        low, high, quick = (1500, 3000, 1200) if known_model else (1200, 2400, 900)
    elif answers.get("works") == "partly" or answers.get("damage") == "major":
        low, high, quick = 400, 1100, 300
    else:
        low, high, quick = 800, 1800, 600

    age_factor = age_price_factor(answers.get("age"))
    low = round_to_nearest_50(low * age_factor)
    high = round_to_nearest_50(high * age_factor)
    quick = round_to_nearest_50(quick * age_factor)

    return {
        "label": f"Sæt prisen til {round_to_nearest_50((low + high) / 2)} kr.",
        "note": f"Cykelestimat baseret på kategori, stand og om producent/model er kendt. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr.",
    }


def is_premium_bicycle(answers):
    text = " ".join(
        str(value or "").lower()
        for value in [
            answers.get("producer"),
            answers.get("producer_name"),
            answers.get("model_name"),
        ]
    )
    text = text.replace("mardone", "madone").replace("etep", "etap")
    producer_hit = any(brand in text for brand in ("trek", "specialized", "cannondale", "pinarello", "cervelo", "bmc", "canyon"))
    premium_hit = any(term in text for term in ("madone", "slr", "etap", "axs", "di2", "dura ace", "ultegra", "carbon", "racercykel"))
    return producer_hit and premium_hit


def estimate_premium_bicycle_price(answers, prices):
    filtered = trim_price_outliers(prices, minimum=12000, maximum=80000)
    working = answers.get("works") == "yes"
    major_issue = answers.get("damage") == "major" or answers.get("works") == "partly"

    if filtered:
        midpoint = filtered[len(filtered) // 2]
        midpoint *= age_price_factor(answers.get("age"))
        midpoint *= damage_price_factor(answers)
        low = round_to_nearest_500(midpoint * (0.75 if major_issue else 0.85))
        high = round_to_nearest_500(midpoint * (1.1 if major_issue else 1.25))
        quick = round_to_nearest_500(midpoint * (0.65 if major_issue else 0.75))
        return {
            "label": f"Sæt prisen til {round_to_nearest_500(midpoint)} kr.",
            "note": f"Pris sat ud fra medianen af lignende premium-racercykler fundet online. Realistisk spænd: {low}-{high} kr. Hurtigt salg kan fx ligge omkring {quick} kr.; årgang, størrelse, hjul, geargruppe og dokumentation betyder meget.",
        }

    if major_issue:
        low, high, quick = 12000, 22000, 10000
    elif working:
        low, high, quick = 24000, 45000, 22000
    else:
        low, high, quick = 18000, 35000, 16000

    age_factor = age_price_factor(answers.get("age"))
    low = round_to_nearest_500(low * age_factor)
    high = round_to_nearest_500(high * age_factor)
    quick = round_to_nearest_500(quick * age_factor)

    return {
        "label": f"Sæt prisen til {round_to_nearest_500((low + high) / 2)} kr.",
        "note": f"Premium-racercykelestimat baseret på producent/model, fordi der ikke blev fundet nok brugbare webpriser. Realistisk spænd: {low}-{high} kr. Kontrollér især årgang, stelstørrelse, hjulsæt, SRAM/Shimano-gruppe og stand. Hurtigt salg kan fx ligge omkring {quick} kr.",
    }


def round_to_nearest_500(value):
    return int(round(float(value) / 500) * 500)


def age_price_factor(age):
    return {
        "newer": 1.05,
        "mid": 1.0,
        "old": 0.75,
        "unknown": 0.9,
    }.get(age, 1.0)


def damage_price_factor(answers):
    if answers.get("damage") == "major" or answers.get("works") == "partly":
        return 0.55
    if answers.get("damage") == "minor":
        return 0.82
    return 1.0

def trim_price_outliers(prices, minimum, maximum):
    filtered = sorted(price for price in prices if minimum <= price <= maximum)
    if len(filtered) >= 5:
        return filtered[1:-1]
    return filtered


def round_to_nearest_25(value):
    return int(round(float(value) / 25) * 25)

def round_to_nearest_50(value):
    return int(round(float(value) / 50) * 50)


def build_ad_text(object_name, assessment, answers, estimate):
    title = build_ad_title(object_name, answers)
    details = build_sale_details(assessment, answers)
    feature_lines = build_ad_feature_lines(assessment, answers)
    condition_lines = build_ad_condition_lines(answers)
    sales_points = build_ad_sales_points(assessment, answers)

    lines = [
        title,
        "",
        f"Pris: {estimate['label']}",
        "",
        f"Jeg sælger {object_name}. Den er vurderet i appen ud fra billeder, producent/model og de oplysninger, der er indtastet.",
    ]

    if sales_points:
        lines.extend(["", "Kort fortalt:"])
        lines.extend([f"- {point}" for point in sales_points])

    if feature_lines:
        lines.extend(["", "Oplysninger:"])
        lines.extend([f"- {line}" for line in feature_lines])

    if details or condition_lines:
        lines.extend(["", "Stand:"])
        if details:
            lines.append(f"- {details}.")
        lines.extend([f"- {line}" for line in condition_lines])

    lines.extend(
        [
            "",
            "Prisforslaget er sat ud fra lignende annoncer og bør sammenholdes med aktuel stand, alder, kvittering, servicehistorik og markedet lige nu.",
            "",
            "Kan afhentes efter aftale. Skriv gerne ved spørgsmål, hvis du vil se flere billeder, eller hvis du ønsker at aftale besigtigelse.",
        ]
    )
    return "\n".join(lines)


def build_ad_title(object_name, answers):
    if is_premium_bicycle(answers):
        return f"{object_name} - high-end racercykel sælges"
    return f"{object_name} sælges"


def build_ad_feature_lines(assessment, answers):
    lines = []
    producer = producer_search_name(answers) or assessment.get("brand")
    model = answers.get("model_name") or assessment.get("model")
    category = assessment.get("category")
    materials = assessment.get("materials") or []

    if producer:
        lines.append(f"Producent/mærke: {normalize_search_terms(str(producer))}")
    if model:
        lines.append(f"Model/serie: {normalize_search_terms(str(model))}")
    if category:
        lines.append(f"Kategori: {category}")
    if materials:
        lines.append(f"Synlige/materialemæssige oplysninger: {', '.join(str(item) for item in materials)}")

    if is_premium_bicycle(answers):
        text = " ".join(str(value or "").lower() for value in [producer, model, answers.get("producer_name")])
        text = text.replace("mardone", "madone").replace("etep", "etap")
        if "madone" in text:
            lines.append("Modeltype: Trek Madone aero-racercykel")
        if "slr" in text:
            lines.append("SLR-serien indikerer Treks lette carbon-topplatform")
        if "etap" in text or "axs" in text:
            lines.append("Geargruppe: elektronisk SRAM eTap/AXS skal kontrolleres og nævnes i annoncen")
        lines.append("Angiv gerne årgang, stelstørrelse, hjulsæt, servicehistorik og kvittering for at styrke annoncen")

    return lines


def build_ad_condition_lines(answers):
    lines = []
    if answers.get("accessories") == "complete":
        lines.append("Tilbehør: komplet ifølge sælgers oplysninger.")
    elif answers.get("accessories") == "partial":
        lines.append("Tilbehør: noget følger med; skriv præcist hvad der er inkluderet.")
    elif answers.get("accessories") == "missing":
        lines.append("Tilbehør: noget mangler; nævn manglerne tydeligt.")

    if answers.get("damage") == "minor":
        lines.append("Der er mindre brugsspor/slitage; tag gerne nærbilleder af de steder, køber bør se.")
    elif answers.get("damage") == "major":
        lines.append("Der er større fejl eller skader; beskriv dem tydeligt og justér prisen derefter.")
    elif answers.get("damage") == "no":
        lines.append("Ingen kendte skader oplyst.")

    return lines


def build_ad_sales_points(assessment, answers):
    if is_premium_bicycle(answers):
        return [
            "Relevant for købere, der søger en let og hurtig landevejs-/aero-racercykel",
            "Producent og model er vigtige for prisen og bør stå tydeligt i titel og første linje",
            "Prisniveau afhænger især af årgang, stelstørrelse, hjul, geargruppe, stand og dokumentation",
        ]

    category = str(assessment.get("category") or "").lower()
    if "cykel" in category:
        return [
            "God til købere, der leder efter en brugbar cykel frem for et reparationsprojekt",
            "Nævn størrelse, gear, bremser og eventuelt service, hvis du kender det",
        ]
    return []

def main():
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print("Serving Cirkulær assistent", flush=True)
    print(f"  Local:   http://127.0.0.1:{PORT}", flush=True)
    for url in get_lan_urls(PORT):
        print(f"  Mobile:  {url}", flush=True)
    if HOST in ("", "0.0.0.0"):
        print("Open one of the Mobile URLs from a phone on the same Wi-Fi.", flush=True)
    server.serve_forever()


def get_lan_urls(port):
    urls = []
    seen = set()
    hostnames = {socket.gethostname(), socket.getfqdn()}

    for hostname in hostnames:
        try:
            addresses = socket.getaddrinfo(hostname, port, type=socket.SOCK_STREAM)
        except socket.gaierror:
            continue

        for family, _, _, _, sockaddr in addresses:
            if family != socket.AF_INET:
                continue
            ip = sockaddr[0]
            if ip.startswith("127.") or ip in seen:
                continue
            seen.add(ip)
            urls.append(f"http://{ip}:{port}")

    return urls


if __name__ == "__main__":
    main()

















