from producer_programs import evaluate_producer_program


ACTION_TEXT = {
    "repair": {
        "label": "Reparer",
        "description": (
            "Undersoeg realistisk reparation foerst: typisk fejl, reservedel, "
            "sikkerhed og forventet levetidsforlaengelse."
        ),
    },
    "sell": {
        "label": "Saelg",
        "description": (
            "Vaelg salg, naar genstanden virker eller har realistisk vaerdi for "
            "en anden bruger. Producentordninger tjekkes som en del af salgsgrenen."
        ),
    },
    "donate": {
        "label": "Bortgiv",
        "description": (
            "Vaelg bortgivelse, naar genstanden stadig kan bruges, men vaerdien "
            "eller salgsindsatsen er lav."
        ),
    },
    "waste": {
        "label": "Affald",
        "description": (
            "Sorter foerst som affald, naar reparation, salg og bortgivelse er "
            "vurderet som urealistiske."
        ),
    },
}

REASON_LABELS = {
    "defect": "Defekt eller virker ikke",
    "no_need": "Bruger den ikke laengere",
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
        "checks": build_checks(possibilities),
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
                assessment.get("waste_category") or "Afhaenger af materiale"
            ),
            "note": (
                "Dette er generel dansk vejledning. Kommunespecifik sortering "
                "maa foerst vises, naar reglen kommer fra en verificeret datakilde."
            ),
        },
    }


def build_context(assessment, answers):
    category = normalize(str(assessment.get("category") or ""))
    materials = [normalize(str(item)) for item in assessment.get("materials", [])]
    condition = answers.get("condition") or assessment.get("condition_estimate")
    producer = answers.get("producer") or assessment.get("brand") or ""

    return {
        "category": category,
        "materials": materials,
        "works": answers.get("works"),
        "reason": answers.get("reason"),
        "age": answers.get("age"),
        "condition": condition or "unknown",
        "damage": answers.get("damage"),
        "accessories": answers.get("accessories"),
        "producer": normalize(str(producer)),
        "object_name": normalize(str(assessment.get("object_name") or "")),
        "subcategory": normalize(str(assessment.get("subcategory") or "")),
        "has_battery": answers.get("battery") == "battery"
        or "batteri" in " ".join(materials),
        "is_electronics": "elektronik" in category or "batteri" in " ".join(materials),
        "is_textile": "tekstil" in category,
        "is_furniture": "moebler" in category or "mobler" in category,
    }


def evaluate_possibilities(context):
    works = context["works"]
    reason = context["reason"]
    damage = context["damage"]
    age = context["age"]
    accessories = context["accessories"]

    repair = (
        works in ("no", "partly", "unknown")
        and damage not in ("major", "worn_out")
        and not (context["is_textile"] and damage in ("major", "worn_out"))
    )
    if context["is_electronics"]:
        repair = repair or works in ("partly", "unknown")

    sell = works == "yes" and damage not in ("major", "worn_out") and reason != "give_away"
    if works == "partly" and context["is_electronics"] and damage != "major":
        sell = True
    if accessories == "complete" and age in ("newer", "mid"):
        sell = sell or works in ("yes", "partly")

    donate = works in ("yes", "partly", "unknown") and damage != "major"
    if reason == "give_away":
        donate = True
    if works == "no" and repair:
        donate = False

    waste = not any([repair, sell, donate])

    return {
        "repair": {
            "realistic": repair,
            "why": "Reparation kontrolleres foerst, hvis fejl og stand goer det realistisk.",
        },
        "sell": {
            "realistic": sell,
            "why": "Salg vurderes, hvis genstanden virker eller har restvaerdi. Producentordninger kontrolleres her.",
        },
        "donate": {
            "realistic": donate,
            "why": "Bortgivelse vurderes, hvis andre sandsynligvis kan bruge genstanden.",
        },
        "waste": {
            "realistic": waste,
            "why": "Affald vaelges kun, naar reparation, salg og bortgivelse ikke er realistiske.",
        },
    }


def choose_action(context, possibilities):
    reason = context["reason"]
    works = context["works"]

    if reason in ("defect", "missing_part"):
        if possibilities["repair"]["realistic"]:
            return "repair", ["Defekt", "Kontroller reparation", "Reparer"]
        return fallback_after_repair(possibilities, ["Defekt", "Reparation ikke realistisk"])

    if reason in ("no_need", "no_space"):
        return prefer_sell_or_donate(possibilities, [REASON_LABELS.get(reason, "Behov")])

    if reason == "replace":
        if works == "yes":
            return prefer_sell_or_donate(possibilities, ["Vil erstatte", "Virker"])
        if possibilities["repair"]["realistic"]:
            return "repair", ["Vil erstatte", "Virker ikke", "Kontroller reparation"]
        return fallback_after_repair(possibilities, ["Vil erstatte", "Reparation ikke realistisk"])

    if reason == "give_away":
        if possibilities["donate"]["realistic"]:
            return "donate", ["Vil give videre", "Bortgiv"]
        return fallback_after_repair(possibilities, ["Vil give videre", "Bortgiv ikke realistisk"])

    if reason == "waste_assumption":
        if possibilities["repair"]["realistic"]:
            return "repair", ["Mener den er affald", "Kontroller reparation", "Reparer"]
        if possibilities["sell"]["realistic"]:
            return "sell", ["Mener den er affald", "Kontroller salg", "Saelg"]
        if possibilities["donate"]["realistic"]:
            return "donate", ["Mener den er affald", "Kontroller bortgivelse", "Bortgiv"]
        return "waste", ["Mener den er affald", "Alle cirkulaere muligheder er nej", "Affald"]

    if works == "yes":
        return prefer_sell_or_donate(possibilities, ["Virker"])
    if possibilities["repair"]["realistic"]:
        return "repair", ["Uklar aarsag", "Kontroller reparation"]
    return fallback_after_repair(possibilities, ["Uklar aarsag"])


def prefer_sell_or_donate(possibilities, path):
    if possibilities["sell"]["realistic"]:
        return "sell", path + ["Vurder salg", "Tjek producentordninger", "Saelg"]
    if possibilities["donate"]["realistic"]:
        return "donate", path + ["Salg lavt", "Bortgiv"]
    return fallback_after_repair(possibilities, path + ["Salg/bortgiv ikke oplagt"])


def fallback_after_repair(possibilities, path):
    for action in ("sell", "donate", "waste"):
        if possibilities[action]["realistic"]:
            label = ACTION_TEXT[action]["label"]
            if action == "sell":
                return action, path + ["Tjek producentordninger", label]
            return action, path + [label]
    return "waste", path + ["Affald"]


def order_actions(recommended_action, possibilities):
    circular_order = ["repair", "sell", "donate", "waste"]
    realistic = [
        action
        for action in circular_order
        if possibilities[action]["realistic"] and action != recommended_action
    ]
    not_realistic = [action for action in circular_order if not possibilities[action]["realistic"]]
    return [recommended_action] + realistic + not_realistic


def build_reasoning(action, context):
    reason = REASON_LABELS.get(context["reason"], "Svarene")
    templates = {
        "repair": (
            f"{reason} peger paa, at reparation skal kontrolleres foerst. "
            "Genstanden behandles derfor som en ressource, indtil reparation viser sig urealistisk."
        ),
        "sell": (
            f"{reason} og svarene tyder paa, at genstanden stadig kan have "
            "brugsvaerdi og oekonomisk vaerdi for en anden. Hvis producenten har en relevant ordning, vises den som en salgsmulighed."
        ),
        "donate": (
            f"{reason} goer bortgivelse til den mest direkte cirkulaere vej, "
            "fordi genstanden sandsynligvis kan bruges videre."
        ),
        "waste": (
            "Affald anbefales foerst efter kontrol af reparation, salg og bortgivelse. "
            "Lokale sorteringsregler skal stadig verificeres."
        ),
    }
    return templates[action]


def build_checks(possibilities):
    return [
        {
            "label": ACTION_TEXT[action]["label"],
            "status": "realistisk" if data["realistic"] else "ikke realistisk",
            "description": data["why"],
        }
        for action, data in possibilities.items()
    ]


def build_impact(action, context):
    repair_estimate = "20-45 kg CO2e" if context["is_electronics"] else "8-25 kg CO2e"
    estimates = {
        "repair": repair_estimate,
        "sell": "15-35 kg CO2e",
        "donate": "10-30 kg CO2e",
        "waste": "reference",
    }
    money = {
        "repair": "mulig udgift",
        "sell": "mulig indtaegt",
        "donate": "0 kr.",
        "waste": "0 kr.",
    }
    return {
        "economy": money[action],
        "co2_saving": estimates[action],
        "note": "Prototypeestimat. Rigtige CO2-tal kraever produkt- og materialedata.",
    }


def confidence_label(context, possibilities):
    realistic_count = sum(1 for item in possibilities.values() if item["realistic"])
    if context["works"] in ("unknown", None) or context["damage"] == "unknown":
        return "Lav-middel"
    if realistic_count > 2:
        return "Middel"
    return "Middel-hoej"


def normalize(value):
    return value.lower().replace("æ", "ae").replace("ø", "oe").replace("å", "aa")

