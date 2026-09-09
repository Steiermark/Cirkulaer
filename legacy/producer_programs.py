PRODUCER_PROGRAMS = [
    {
        "id": "ikea_gensalg",
        "producer": "ikea",
        "title": "IKEA Gensalg",
        "scheme_types": ["buy_back", "resale"],
        "url": "https://www.ikea.com/dk/da/second-hand/sell-to-ikea/quote/",
        "reward": "IKEA tilgodebevis",
        "source_label": "IKEA Gensalg vurderingsværktøj",
        "eligible_hints": [
            "kommode",
            "skaenk",
            "vitrineskab",
            "bogreol",
            "reol",
            "garderobeskab",
            "multimediemoebel",
            "spisebord",
            "skrivebord",
            "sofabord",
            "sengebord",
            "stol",
            "kontorstol",
            "taburet",
            "sengestel",
            "underseng",
            "lamelbund",
            "entre",
            "hylde",
            "opbevaring",
            "bordlampe",
            "gulvlampe",
            "belysning",
            "ovnfast",
            "glas",
            "porcelaen",
            "boernemoebel",
            "baby",
        ],
        "excluded_hints": [
            "hvidevare",
            "elektrisk",
            "elektronik",
            "madras",
            "sengetekstil",
            "havemoebel",
            "udendoers",
            "koekken",
            "bordplade",
            "front",
            "loes del",
        ],
        "checks": [
            "Originalt producentprodukt",
            "God/salgbar stand",
            "Rent og uændret",
            "Komplet og fuldt funktionelt",
            "Korrekt samlet",
            "Omfattet produktkategori",
        ],
    }
]


def find_program_candidates(assessment):
    producer = normalize(str(assessment.get("brand") or ""))
    text = normalize(
        " ".join(
            str(assessment.get(key) or "")
            for key in ("object_name", "category", "subcategory")
        )
    )
    candidates = []
    for program in PRODUCER_PROGRAMS:
        if producer != program["producer"]:
            continue
        eligible = any(hint in text for hint in program["eligible_hints"])
        excluded = any(hint in text for hint in program["excluded_hints"])
        if eligible and not excluded:
            candidates.append(program["id"])
    return candidates


def find_producer_programs(context):
    producer = context.get("producer") or ""
    if not producer or producer in ("unknown", "ved ikke", "no", "anden"):
        return []

    return [program for program in PRODUCER_PROGRAMS if program["producer"] == producer]


def evaluate_producer_program(context):
    programs = find_producer_programs(context)
    if not programs:
        return {
            "status": "none",
            "title": "Producentordninger",
            "message": (
                "Der er ikke fundet en konkret producentordning endnu. "
                "I næste fase kan modulet slå op i en database med reparation, "
                "reservedele, buy-back, trade-in, take-back og refurbishment."
            ),
            "programs": [],
        }

    evaluated = [evaluate_single_program(program, context) for program in programs]
    best = sorted(evaluated, key=lambda item: item["rank"], reverse=True)[0]

    return {
        "status": best["status"],
        "title": "Producentordning fundet",
        "message": best["message"],
        "programs": evaluated,
    }


def evaluate_single_program(program, context):
    text = " ".join(
        [context.get("object_name", ""), context.get("category", ""), context.get("subcategory", "")]
    )
    has_eligible_hint = any(hint in text for hint in program["eligible_hints"])
    has_excluded_hint = any(hint in text for hint in program["excluded_hints"])
    good_condition = context.get("works") == "yes" and context.get("damage") in ("no", "minor", None)
    complete = context.get("accessories") in ("complete", "irrelevant", None)
    original = tri_state(context.get("original_product"))
    clean = tri_state(context.get("clean_state"))
    unmodified = tri_state(context.get("unmodified"))
    assembled = tri_state(context.get("assembled"))

    eligibility_checks = [original, clean, unmodified, assembled]
    likely = (
        has_eligible_hint
        and not has_excluded_hint
        and good_condition
        and complete
        and all(value is True for value in eligibility_checks)
    )
    possible = (
        not has_excluded_hint
        and context.get("works") in ("yes", "unknown")
        and not any(value is False for value in eligibility_checks)
    )

    if likely:
        status = "likely"
        rank = 3
        message = (
            f"{program['title']} ser ud til potentielt at være relevant. "
            "Hent en officiel vurdering og sammenlign med almindeligt privat salg."
        )
    elif possible:
        status = "possible"
        rank = 2
        message = (
            f"{program['title']} kan muligvis være relevant, men stand, kategori, "
            "original mærkning og komplethed skal bekræftes hos producenten."
        )
    else:
        status = "unlikely"
        rank = 1
        message = (
            f"{program['title']} ligner ikke en oplagt vej ud fra svarene. "
            "Fortsæt med almindelig salgs- eller bortgivelsesvurdering."
        )

    return {
        "id": program["id"],
        "title": program["title"],
        "producer": program["producer"].upper(),
        "scheme_types": program["scheme_types"],
        "status": status,
        "rank": rank,
        "message": message,
        "url": program["url"],
        "reward": program["reward"],
        "source_label": program["source_label"],
        "checks": [
            {"label": "Originalt producentprodukt", "ok": original},
            {"label": "God/salgbar stand", "ok": good_condition},
            {"label": "Rent", "ok": clean},
            {"label": "Komplet", "ok": complete},
            {"label": "Uændret", "ok": unmodified},
            {"label": "Korrekt samlet", "ok": assembled},
            {"label": "Mulig omfattet kategori", "ok": has_eligible_hint and not has_excluded_hint},
        ],
        "comparison": [
            {"label": program["title"], "value": program["reward"]},
            {"label": "Privat salg", "value": "ofte højere pris, mere arbejde"},
            {"label": "Hurtigt salg", "value": "lavere pris, hurtigere afhændelse"},
        ],
    }


def tri_state(value):
    if value == "yes":
        return True
    if value == "no":
        return False
    return None


def normalize(value):
    return value.lower().replace("æ", "ae").replace("ø", "oe").replace("å", "aa")
