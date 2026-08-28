ACTION_TEXT = {
    "repair": {
        "label": "Reparér",
        "description": (
            "Start med at undersøge en enkel reparation, reservedel eller lokal "
            "reparationsmulighed."
        ),
    },
    "sell": {
        "label": "Sælg",
        "description": (
            "Genstanden ser ud til at have brugsværdi nok til, at salg kan være "
            "næste skridt."
        ),
    },
    "donate": {
        "label": "Bortgiv",
        "description": (
            "Hvis værdien er lav, men genstanden stadig kan bruges, er bortgivelse "
            "ofte bedst."
        ),
    },
    "waste": {
        "label": "Affald",
        "description": (
            "Brug affaldsløsningen som sidste mulighed, når fortsat brug ikke "
            "virker realistisk."
        ),
    },
}


def score_actions(assessment, answers):
    category = str(assessment.get("category") or "")
    is_electronics = "Elektronik" in category
    is_textile = "Tekstil" in category
    scores = {
        "repair": 60 if is_electronics else 38,
        "sell": 45,
        "donate": 42,
        "waste": 12,
    }

    works = answers.get("works")
    reason = answers.get("reason")
    damage = answers.get("damage")
    battery = answers.get("battery")

    if works == "yes":
        scores["sell"] += 30
        scores["donate"] += 22
        scores["repair"] -= 22

    if works == "partly":
        scores["repair"] += 24
        scores["sell"] -= 8
        scores["donate"] += 4

    if works == "no":
        scores["repair"] += 18 if is_electronics else 8
        scores["sell"] -= 24
        scores["donate"] -= 18
        scores["waste"] += 22

    if reason == "no_need":
        scores["sell"] += 22
        scores["donate"] += 18
        scores["repair"] -= 12

    if reason in ("defect", "missing_part"):
        scores["repair"] += 22
        scores["sell"] -= 12
        scores["waste"] += 10

    if reason == "worn":
        scores["donate"] += 8 if is_textile else 2
        scores["waste"] += 8

    if damage == "major":
        scores["waste"] += 25
        scores["sell"] -= 24
        scores["donate"] -= 20

    if battery == "battery":
        scores["waste"] += 8

    return scores


def build_recommendation(assessment, answers):
    scores = score_actions(assessment, answers)
    ordered_actions = sorted(scores, key=lambda action: scores[action], reverse=True)
    recommended_action = ordered_actions[0]

    reasoning = {
        "repair": (
            "Den bedste første handling er at undersøge reparation, fordi "
            "genstanden enten er defekt, delvist fungerende eller typisk kan få "
            "forlænget levetid med en begrænset indsats."
        ),
        "sell": (
            "Den bedste første handling er salg, fordi genstanden sandsynligvis "
            "stadig fungerer og kan have værdi for en anden bruger."
        ),
        "donate": (
            "Den bedste første handling er bortgivelse, fordi genstanden kan have "
            "brugsværdi, selv om den økonomiske værdi kan være begrænset."
        ),
        "waste": (
            "Affald er valgt som sidste mulighed, fordi svarene peger på lav "
            "brugsværdi eller betydelig skade. Lokale regler bør bekræftes før "
            "aflevering."
        ),
    }

    return {
        "object_name": str(assessment.get("object_name") or "Ukendt genstand"),
        "recommended_action": recommended_action,
        "scores": scores,
        "reasoning": reasoning[recommended_action],
        "options": [
            {
                "key": action,
                "label": ACTION_TEXT[action]["label"],
                "description": ACTION_TEXT[action]["description"],
            }
            for action in ordered_actions
        ],
        "waste": {
            "general_fraction": str(
                assessment.get("waste_category") or "Afhænger af materiale"
            ),
            "note": (
                "Dette er en generel fraktion. Kommunespecifik sortering kræver "
                "verificerede lokale regler."
            ),
        },
    }
