import itertools
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "legacy"))

import server
from decision_engine import build_recommendation

# The sale fixture keeps only keys that do not depend on live search results, so the
# real scrape is pure latency here. Stubbing it also makes the dump reproducible.
server.search_price_signals = lambda query, include_reshopper=False: {
    "url": "https://www.google.com/search?q=stub",
    "prices": [],
    "signals": [],
    "comparables": [],
    "confidence": "lav",
    "note": "stub",
}

from server import build_sale_assist  # noqa: E402  (must follow the stub above)

ASSESSMENTS = [
    {"object_name": "Kontorstol", "category": "Møbler og indbo", "category_id": "furniture",
     "waste_category": "Storskrald", "materials": ["metal", "tekstil"], "brand": "IKEA",
     "model": "MARKUS", "subcategory": "Kontorstol", "confidence": 0.9,
     "visible_damage": [], "condition_estimate": "good", "uncertainty_notes": []},
    {"object_name": "Akkuboremaskine", "category": "Elektronik og værktøj", "category_id": "electronics",
     "waste_category": "Småt elektronik", "materials": ["plast", "metal", "batteri"], "brand": "Bosch",
     "model": "PSR 18", "subcategory": "Boremaskine", "confidence": 0.8,
     "visible_damage": ["ridser"], "condition_estimate": "worn", "uncertainty_notes": []},
    {"object_name": "Racercykel", "category": "Cykler", "category_id": "bicycle",
     "waste_category": "Metal", "materials": ["carbon"], "brand": "Trek", "model": "Madone SLR",
     "subcategory": "Racercykel", "confidence": 0.85,
     "visible_damage": [], "condition_estimate": "good", "uncertainty_notes": []},
    {"object_name": "Barnevogn", "category": "Børn og baby", "category_id": "other",
     "waste_category": "Storskrald", "materials": ["plast", "tekstil"], "brand": None,
     "model": None, "subcategory": "Barnevogn", "confidence": 0.6,
     "visible_damage": ["knækket håndtag"], "condition_estimate": "damaged", "uncertainty_notes": []},
]

WORKS = ["yes", "no", "partly", "unknown", None]
REASONS = ["defect", "no_need", "replace", "no_space", "give_away", "waste_assumption", "worn", "missing_part", None]
DAMAGE = ["no", "minor", "major", "worn_out", "unknown", None]
AGE = ["newer", "mid", "old", "unknown", None]
ACCESSORIES = ["complete", "partial", "irrelevant", None]
SAFETY = ["risk", "ok", "unknown", None]
CLEANING = ["light", "deep", "unknown", None]

BASE = {"works": "yes", "reason": "no_need", "damage": "no",
        "age": "mid", "accessories": "complete", "safety": "ok"}


def strip_none(answers):
    return {key: value for key, value in answers.items() if value is not None}


def answer_cases():
    """Three targeted sweeps instead of a full cartesian product.

    Sweep A drives the decision tree: every works x reason x damage combination.
    Sweep B drives confidence and the price factors: age x accessories x safety.
    Sweep C drives the furniture clean branch, unreachable unless cleaning is set.
    Together they hit every recommended_action without generating 21k redundant cases.
    """
    seen = set()

    for works, reason, damage in itertools.product(WORKS, REASONS, DAMAGE):
        answers = {**BASE, "works": works, "reason": reason, "damage": damage,
                   "producer": "detected"}
        key = tuple(sorted(strip_none(answers).items()))
        if key not in seen:
            seen.add(key)
            yield strip_none(answers)

    for age, accessories, safety in itertools.product(AGE, ACCESSORIES, SAFETY):
        answers = {**BASE, "age": age, "accessories": accessories, "safety": safety,
                   "producer": "detected"}
        key = tuple(sorted(strip_none(answers).items()))
        if key not in seen:
            seen.add(key)
            yield strip_none(answers)

    # Sweep C: the furniture clean/prepare branch, which is only reachable when
    # `cleaning` is answered. Without this sweep no case ever recommends "clean".
    for cleaning, works, reason in itertools.product(CLEANING, WORKS, REASONS):
        answers = {**BASE, "cleaning": cleaning, "works": works, "reason": reason,
                   "producer": "detected"}
        key = tuple(sorted(strip_none(answers).items()))
        if key not in seen:
            seen.add(key)
            yield strip_none(answers)


def main():
    out_dir = os.path.join(os.path.dirname(__file__), "..", "src", "Api.Tests", "fixtures")
    os.makedirs(out_dir, exist_ok=True)

    cases = list(answer_cases())

    decision = {
        "assessments": ASSESSMENTS,
        "cases": [
            {
                "name": f"a{a_index}-c{c_index}",
                "assessment_index": a_index,
                "answers": answers,
                "expected": build_recommendation(ASSESSMENTS[a_index], answers),
            }
            for a_index in range(len(ASSESSMENTS))
            for c_index, answers in enumerate(cases)
        ],
    }

    with open(os.path.join(out_dir, "decision-cases.json"), "w", encoding="utf-8") as handle:
        json.dump(decision, handle, ensure_ascii=False, indent=2)

    volatile = ("price", "price_note", "search_note", "search_url",
                "signals", "comparables", "price_confidence")
    sale_subset = cases[::7]
    sale = {
        "assessments": ASSESSMENTS,
        "cases": [],
    }
    for a_index in range(len(ASSESSMENTS)):
        for c_index, answers in enumerate(sale_subset):
            recommendation = build_recommendation(ASSESSMENTS[a_index], answers)
            result = build_sale_assist(ASSESSMENTS[a_index], answers, recommendation)
            for key in volatile:
                result.pop(key, None)
            sale["cases"].append({
                "name": f"a{a_index}-c{c_index}",
                "assessment_index": a_index,
                "answers": answers,
                "expected": result,
            })

    with open(os.path.join(out_dir, "sale-cases.json"), "w", encoding="utf-8") as handle:
        json.dump(sale, handle, ensure_ascii=False, indent=2)

    print(f"answer cases: {len(cases)}")
    print(f"decision cases: {len(decision['cases'])}")
    print(f"sale cases: {len(sale['cases'])}")


if __name__ == "__main__":
    main()
