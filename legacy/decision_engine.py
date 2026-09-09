from producer_programs import evaluate_producer_program


ACTION_TEXT = {
    "repair": {
        "label": "Reparér",
        "description": (
            "Undersøg realistisk reparation først: typisk fejl, reservedel, "
            "sikkerhed og forventet levetidsforlængelse."
        ),
    },
    "clean": {
        "label": "Rens/klargør",
        "description": (
            "For møbler kan rensning, pletbehandling eller enkel klargøring "
            "skabe ny værdi før salg eller bortgivelse."
        ),
    },
    "sell": {
        "label": "Sælg",
        "description": (
            "Vælg salg, når genstanden virker eller har realistisk værdi for "
            "en anden bruger. Producentordninger tjekkes som en del af salgsgrenen."
        ),
    },
    "donate": {
        "label": "Bortgiv",
        "description": (
            "Vælg bortgivelse, når genstanden stadig kan bruges, men værdien "
            "eller salgsindsatsen er lav."
        ),
    },
    "waste": {
        "label": "Affald",
        "description": (
            "Sortér først som affald, når reparation, rensning, salg og "
            "bortgivelse er vurderet som urealistiske."
        ),
    },
}

REASON_LABELS = {
    "defect": "Defekt eller virker ikke",
    "no_need": "Bruger den ikke længere",
    "replace": "Vil erstatte den",
    "no_space": "Har ikke plads",
    "give_away": "Vil give den videre",
    "waste_assumption": "Mener den er affald",
    "worn": "Slidt",
    "missing_part": "Mangler en del",
}


def build_recommendation(assessment, answers):
    context = build_context(assessment, answers)
    possibilities = evaluate_possibilities(context)
    recommended_action, decision_path = choose_action(context, possibilities)
    ordered_actions = order_actions(recommended_action, possibilities)

    return {
        "object_name": str(assessment.get("object_name") or "Ukendt genstand"),
        "recommended_action": recommended_action,
        "decision_path": decision_path,
        "confidence": confidence_label(context, possibilities),
        "reasoning": build_reasoning(recommended_action, context),
        "checks": build_checks(possibilities, ordered_actions, recommended_action),
        "options": [
            {
                "key": action,
                "label": ACTION_TEXT[action]["label"],
                "description": ACTION_TEXT[action]["description"],
                "realistic": possibilities[action]["realistic"],
            }
            for action in ordered_actions
        ],
        "impact": build_impact(recommended_action, context),
        "producer_program": evaluate_producer_program(context),
        "waste": {
            "general_fraction": str(
                assessment.get("waste_category") or "Afhænger af materiale"
            ),
            "note": (
                "Dette er generel dansk vejledning. Kommunespecifik sortering "
                "må først vises, når reglen kommer fra en verificeret datakilde."
            ),
        },
    }


def build_context(assessment, answers):
    category = normalize(str(assessment.get("category") or ""))
    category_id = str(assessment.get("category_id") or "").lower()
    materials = [normalize(str(item)) for item in assessment.get("materials", [])]
    condition = answers.get("condition") or assessment.get("condition_estimate")
    selected_producer = answers.get("producer")
    if selected_producer in (None, "", "detected"):
        producer = assessment.get("brand") or ""
    elif selected_producer == "other" and answers.get("producer_name"):
        producer = answers.get("producer_name")
    elif selected_producer in ("unknown", "ved ikke"):
        producer = ""
    else:
        producer = selected_producer
    model = answers.get("model_name") or assessment.get("model") or ""
    damage = answers.get("damage") or infer_damage_from_assessment(assessment)
    safety = answers.get("safety") or "unknown"

    return {
        "category": category,
        "category_id": category_id,
        "materials": materials,
        "works": answers.get("works"),
        "reason": answers.get("reason"),
        "age": answers.get("age"),
        "condition": condition or "unknown",
        "damage": damage,
        "cleaning": answers.get("cleaning"),
        "accessories": answers.get("accessories"),
        "producer": normalize(str(producer)),
        "model": normalize(str(model)),
        "object_name": normalize(str(assessment.get("object_name") or "")),
        "subcategory": normalize(str(assessment.get("subcategory") or "")),
        "identification_confidence": assessment.get("confidence"),
        "safety": safety,
        "safety_risk": safety == "risk",
        "original_product": answers.get("original_product"),
        "clean_state": answers.get("clean_state"),
        "unmodified": answers.get("unmodified"),
        "assembled": answers.get("assembled"),
        "has_battery": answers.get("battery") == "battery"
        or "batteri" in " ".join(materials),
        "is_electronics": category_id == "electronics" or "elektronik" in category or "batteri" in " ".join(materials),
        "is_textile": category_id == "textile" or "tekstil" in category,
        "is_furniture": category_id == "furniture" or "moebler" in category or "mobler" in category or "moebel" in category,
    }


def infer_damage_from_assessment(assessment):
    visible = " ".join(str(item).lower() for item in assessment.get("visible_damage", []))
    condition = str(assessment.get("condition_estimate") or "").lower()
    major_terms = (
        "knækket", "knust", "revnet", "brændt", "laekker", "lækker",
        "deformeret", "gennemtæret", "gennemtæret",
    )
    if any(term in visible for term in major_terms):
        return "major"
    if visible or condition in ("worn", "damaged"):
        return "minor"
    if condition in ("new", "good"):
        return "no"
    return "unknown"


def evaluate_possibilities(context):
    works = context["works"]
    reason = context["reason"]
    damage = context["damage"]
    age = context["age"]
    accessories = context["accessories"]
    cleaning = context["cleaning"]

    repair = (
        works in ("no", "partly", "unknown")
        and damage not in ("major", "worn_out")
        and not (context["is_textile"] and damage in ("major", "worn_out"))
    )
    if context["is_electronics"]:
        repair = repair or works in ("partly", "unknown")
    if context["is_furniture"] and works == "yes":
        repair = False

    clean = (
        context["is_furniture"]
        and works in ("yes", "unknown")
        and damage not in ("major", "worn_out")
        and cleaning in ("light", "deep", "unknown")
    )

    sell = works == "yes" and damage not in ("major", "worn_out") and reason != "give_away"
    if works == "partly" and context["is_electronics"] and damage != "major":
        sell = True
    if accessories == "complete" and age in ("newer", "mid"):
        sell = sell or works in ("yes", "partly")
    if clean:
        sell = True

    donate = works in ("yes", "partly", "unknown") and damage != "major"
    if reason == "give_away":
        donate = True
    if works == "no" and repair:
        donate = False

    if context["safety_risk"]:
        repair = clean = sell = donate = False

    waste = context["safety_risk"] or not any([repair, clean, sell, donate])

    age_modifier = {"newer": 8, "mid": 3, "old": -8, "unknown": -2}.get(age, -2)
    scores = {
        "repair": 72 + (8 if works == "partly" else 0) + age_modifier,
        "clean": 70 + (8 if cleaning in ("light", "deep") else 0),
        "sell": 72 + age_modifier + (8 if accessories == "complete" else 0),
        "donate": 62 + (8 if reason == "give_away" else 0),
        "waste": 95 if context["safety_risk"] else 70,
    }

    return {
        "repair": {
            "realistic": repair,
            "score": scores["repair"] if repair else 0,
            "why": "Reparation kontrolleres først, hvis fejl og stand gør det realistisk.",
        },
        "clean": {
            "realistic": clean,
            "score": scores["clean"] if clean else 0,
            "why": "For møbler kan rensning eller klargøring skabe værdi før salg eller bortgivelse.",
        },
        "sell": {
            "realistic": sell,
            "score": scores["sell"] if sell else 0,
            "why": "Salg vurderes, hvis genstanden virker eller har restværdi. Producentordninger kontrolleres her.",
        },
        "donate": {
            "realistic": donate,
            "score": scores["donate"] if donate else 0,
            "why": "Bortgivelse vurderes, hvis andre sandsynligvis kan bruge genstanden.",
        },
        "waste": {
            "realistic": waste,
            "score": scores["waste"] if waste else 0,
            "why": "Affald vælges kun, når reparation, rensning, salg og bortgivelse ikke er realistiske.",
        },
    }


def choose_action(context, possibilities):
    reason = context["reason"]
    works = context["works"]

    if context["safety_risk"]:
        return "waste", ["Mulig sikkerhedsrisiko", "Undgå videre brug", "Sikker aflevering"]

    if reason in ("defect", "missing_part"):
        if possibilities["repair"]["realistic"]:
            return "repair", ["Defekt", "Kontrollér reparation", "Reparér"]
        return "waste", ["Defekt", "Reparation ikke realistisk", "Affald"]

    if reason in ("no_need", "no_space"):
        return prefer_clean_sell_or_donate(possibilities, [REASON_LABELS.get(reason, "Behov")])

    if reason == "replace":
        if works == "yes":
            return prefer_clean_sell_or_donate(possibilities, ["Vil erstatte", "Virker"])
        if possibilities["repair"]["realistic"]:
            return "repair", ["Vil erstatte", "Virker ikke", "Kontrollér reparation"]
        return fallback_after_repair(possibilities, ["Vil erstatte", "Reparation ikke realistisk"])

    if reason == "give_away":
        if possibilities["clean"]["realistic"]:
            return "clean", ["Vil give videre", "Rens/klargør", "Bortgiv"]
        if possibilities["donate"]["realistic"]:
            return "donate", ["Vil give videre", "Bortgiv"]
        return fallback_after_repair(possibilities, ["Vil give videre", "Bortgiv ikke realistisk"])

    if reason == "waste_assumption":
        if possibilities["repair"]["realistic"]:
            return "repair", ["Mener den er affald", "Kontrollér reparation", "Reparér"]
        if possibilities["clean"]["realistic"]:
            return "clean", ["Mener den er affald", "Kontrollér rensning", "Rens/klargør"]
        if possibilities["sell"]["realistic"]:
            return "sell", ["Mener den er affald", "Kontrollér salg", "Sælg"]
        if possibilities["donate"]["realistic"]:
            return "donate", ["Mener den er affald", "Kontrollér bortgivelse", "Bortgiv"]
        return "waste", ["Mener den er affald", "Alle cirkulære muligheder er nej", "Affald"]

    if works == "yes":
        return prefer_clean_sell_or_donate(possibilities, ["Virker"])
    if possibilities["repair"]["realistic"]:
        return "repair", ["Uklar årsag", "Kontrollér reparation"]
    return fallback_after_repair(possibilities, ["Uklar årsag"])


def prefer_clean_sell_or_donate(possibilities, path):
    if possibilities["clean"]["realistic"]:
        return "clean", path + ["Rens/klargør", "Vurder salg"]
    if possibilities["sell"]["realistic"]:
        return "sell", path + ["Vurder salg", "Tjek producentordninger", "Sælg"]
    if possibilities["donate"]["realistic"]:
        return "donate", path + ["Salg lavt", "Bortgiv"]
    return fallback_after_repair(possibilities, path + ["Salg/bortgiv ikke oplagt"])


def fallback_after_repair(possibilities, path):
    for action in ("clean", "sell", "donate", "waste"):
        if possibilities[action]["realistic"]:
            label = ACTION_TEXT[action]["label"]
            if action == "sell":
                return action, path + ["Tjek producentordninger", label]
            return action, path + [label]
    return "waste", path + ["Affald"]


def order_actions(recommended_action, possibilities):
    if recommended_action == "waste":
        return ["waste", "donate", "sell"]

    circular_order = ["repair", "clean", "sell", "donate", "waste"]
    realistic = [
        action
        for action in circular_order
        if possibilities[action]["realistic"] and action != recommended_action
    ]
    realistic.sort(key=lambda action: (-possibilities[action]["score"], circular_order.index(action)))
    not_realistic = [action for action in circular_order if not possibilities[action]["realistic"]]
    return [recommended_action] + realistic + not_realistic


def build_reasoning(action, context):
    reason = REASON_LABELS.get(context["reason"], "Svarene")
    templates = {
        "repair": (
            f"{reason} peger på, at reparation skal kontrolleres først. "
            "Genstanden behandles derfor som en ressource, indtil reparation viser sig urealistisk."
        ),
        "clean": (
            f"{reason} og møbelkategorien peger på, at rensning eller klargøring "
            "kan skabe værdi før salg eller bortgivelse. Det er derfor næste bedste cirkulære handling."
        ),
        "sell": (
            f"{reason} og svarene tyder på, at genstanden stadig kan have "
            "brugsværdi og økonomisk værdi for en anden. Hvis producenten har en relevant ordning, vises den som en salgsmulighed."
        ),
        "donate": (
            f"{reason} gør bortgivelse til den mest direkte cirkulære vej, "
            "fordi genstanden sandsynligvis kan bruges videre."
        ),
        "waste": (
            "Affald anbefales, når genstanden er defekt og reparation ikke er realistisk, eller når reparation, rensning, salg og bortgivelse ikke er realistiske. "
            "Lokale sorteringsregler skal stadig verificeres."
        ),
    }
    if context["safety_risk"]:
        return (
            "Svarene tyder på en mulig sikkerhedsrisiko. Genstanden bør ikke sælges, "
            "bortgives eller forsøges repareret uden faglig vurdering; vælg sikker aflevering."
        )
    return templates[action]


def build_checks(possibilities, ordered_actions, recommended_action):
    return [
        {
            "label": ACTION_TEXT[action]["label"],
            "status": "realistisk"
            if action == recommended_action or possibilities[action]["realistic"]
            else "ikke realistisk",
            "description": possibilities[action]["why"],
        }
        for action in ordered_actions
    ]


def build_impact(action, context):
    money = {
        "repair": "mulig udgift",
        "clean": "lav udgift / højere værdi",
        "sell": "mulig indtægt",
        "donate": "0 kr.",
        "waste": "0 kr.",
    }
    return {
        "economy": money[action],
        "co2_saving": "Ikke beregnet",
        "note": "CO2-effekten vises først, når produkt-, materiale- og levetidsdata kan dokumenteres.",
    }


def confidence_label(context, possibilities):
    required_values = [
        context["reason"],
        context["works"],
        context["damage"],
        context["age"],
        context["accessories"],
        context["safety"],
    ]
    if any(value in (None, "", "unknown") for value in required_values):
        return "Lav"
    identification_confidence = context.get("identification_confidence")
    if isinstance(identification_confidence, (int, float)):
        if identification_confidence < 0.5:
            return "Lav"
        if identification_confidence < 0.75:
            return "Middel"
    realistic_count = sum(1 for item in possibilities.values() if item["realistic"])
    if realistic_count > 2:
        return "Middel"
    return "Høj"


def normalize(value):
    return value.lower().replace("æ", "ae").replace("ø", "oe").replace("å", "aa")





