import unittest

from decision_engine import build_context, build_recommendation
from server import (
    build_sale_query,
    build_test_assessment,
    canonical_category_id,
    estimate_bicycle_price,
    extract_comparables,
    extract_prices,
    is_reshopper_relevant,
)


class DecisionEngineTest(unittest.TestCase):
    def test_plural_furniture_category_gets_canonical_id(self):
        self.assertEqual(canonical_category_id("Møbler og indbo"), "furniture")

    def test_billy_scenario_identifies_ikea_brand_and_model(self):
        assessment = build_test_assessment("billy")

        self.assertEqual(assessment["object_name"], "BILLY-reol")
        self.assertEqual(assessment["brand"], "IKEA")
        self.assertEqual(assessment["model"], "BILLY")
        self.assertEqual(assessment["subcategory"], "Bogreol")

    def test_no_longer_used_working_item_should_be_sold_first(self):
        assessment = {
            "object_name": "Kontorstol",
            "category": "Møbler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["metal", "tekstil"],
        }
        answers = {
            "works": "yes",
            "reason": "no_need",
            "damage": "no",
            "age": "mid",
            "accessories": "complete",
            "producer": "other",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "sell")
        self.assertEqual(
            result["decision_path"],
            ["Bruger den ikke længere", "Vurder salg", "Tjek producentordninger", "Sælg"],
        )

    def test_defective_electronics_should_prioritize_repair(self):
        assessment = {
            "object_name": "Akkuboremaskine",
            "category": "Elektronik og værktøj",
            "waste_category": "Småt elektronik",
            "materials": ["plast", "metal", "batteri"],
        }
        answers = {
            "works": "no",
            "reason": "defect",
            "battery": "battery",
            "damage": "minor",
            "age": "mid",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "repair")
        self.assertIn("Kontrollér reparation", result["decision_path"])

    def test_user_thinks_waste_but_working_item_should_not_go_to_waste(self):
        assessment = {
            "object_name": "Kaffemaskine",
            "category": "Elektronik",
            "waste_category": "Småt elektronik",
            "materials": ["plast", "metal"],
        }
        answers = {
            "works": "yes",
            "reason": "waste_assumption",
            "battery": "cord",
            "damage": "no",
            "age": "mid",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "sell")
        self.assertEqual(result["decision_path"][1], "Kontrollér salg")

    def test_heavily_damaged_non_electronics_can_end_as_waste(self):
        assessment = {
            "object_name": "Reol",
            "category": "Møbler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["træ"],
        }
        answers = {
            "works": "no",
            "reason": "waste_assumption",
            "damage": "major",
            "age": "old",
            "accessories": "missing",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "waste")
        self.assertIn("Alle cirkulære muligheder er nej", result["decision_path"])
        self.assertEqual([option["key"] for option in result["options"]], ["waste", "donate", "sell"])
        self.assertEqual([check["label"] for check in result["checks"]], ["Affald", "Bortgiv", "Sælg"])
        self.assertEqual(result["checks"][0]["status"], "realistisk")
        self.assertEqual(result["checks"][0]["status"], "realistisk")

    def test_defective_item_without_realistic_repair_becomes_waste(self):
        assessment = {
            "object_name": "Ødelagt reol",
            "category": "Møbler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["træ"],
        }
        answers = {
            "works": "no",
            "reason": "defect",
            "damage": "major",
            "cleaning": "deep",
            "age": "old",
            "accessories": "missing",
            "producer": "other",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "waste")
        self.assertEqual(
            result["decision_path"],
            ["Defekt", "Reparation ikke realistisk", "Affald"],
        )
        self.assertEqual([option["key"] for option in result["options"]], ["waste", "donate", "sell"])
        self.assertEqual([check["label"] for check in result["checks"]], ["Affald", "Bortgiv", "Sælg"])
        self.assertEqual(result["checks"][0]["status"], "realistisk")
        self.assertEqual(result["checks"][0]["status"], "realistisk")

    def test_furniture_can_prioritize_cleaning_before_sale(self):
        assessment = {
            "object_name": "Sofa",
            "category": "Møbler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["tekstil", "træ"],
        }
        answers = {
            "works": "yes",
            "reason": "no_need",
            "damage": "minor",
            "cleaning": "deep",
            "age": "mid",
            "accessories": "irrelevant",
            "producer": "other",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "clean")
        self.assertIn("Rens/klargør", result["decision_path"])
        self.assertEqual(result["options"][1]["key"], "sell")
    def test_ikea_resale_is_a_producer_program_not_core_sell_logic(self):
        assessment = {
            "object_name": "BILLY bogreol",
            "category": "Møbler og indbo",
            "brand": "IKEA",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["træ"],
        }
        answers = {
            "works": "yes",
            "reason": "no_need",
            "damage": "no",
            "age": "mid",
            "accessories": "complete",
            "producer": "ikea",
            "original_product": "yes",
            "clean_state": "yes",
            "unmodified": "yes",
            "assembled": "yes",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "sell")
        self.assertEqual(result["producer_program"]["status"], "likely")
        self.assertEqual(result["producer_program"]["programs"][0]["id"], "ikea_gensalg")

    def test_ikea_program_stays_possible_until_requirements_are_confirmed(self):
        assessment = {
            "object_name": "BILLY bogreol",
            "category": "Møbler og indbo",
            "category_id": "furniture",
            "brand": "IKEA",
            "waste_category": "Storskrald",
            "materials": ["træ"],
        }
        answers = {
            "works": "yes",
            "reason": "no_need",
            "damage": "no",
            "age": "mid",
            "accessories": "complete",
            "producer": "ikea",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["producer_program"]["status"], "possible")
        self.assertIsNone(result["producer_program"]["programs"][0]["checks"][0]["ok"])

    def test_detected_producer_is_kept_when_user_confirms_it(self):
        assessment = {
            "object_name": "Radio",
            "category": "Elektronik",
            "brand": "Sony",
            "model": "X1",
            "materials": [],
        }

        context = build_context(assessment, {"producer": "detected"})

        self.assertEqual(context["producer"], "sony")

    def test_detected_marker_is_not_written_into_sale_query(self):
        assessment = {
            "object_name": "Radio",
            "category": "Elektronik",
            "brand": "Sony",
            "model": "X1",
        }

        query = build_sale_query(assessment, {"producer": "detected"})

        self.assertEqual(query, "Sony X1 Radio brugt pris Danmark")

    def test_visible_ai_damage_is_used_when_user_has_not_answered(self):
        assessment = {
            "object_name": "Reol",
            "category": "Møbler",
            "visible_damage": ["Knækket hylde"],
            "condition_estimate": "damaged",
            "materials": ["træ"],
        }

        context = build_context(assessment, {})

        self.assertEqual(context["damage"], "major")

    def test_safety_risk_overrides_circular_actions(self):
        assessment = {
            "object_name": "Batteri",
            "category": "Elektronik",
            "category_id": "electronics",
            "materials": ["batteri"],
            "waste_category": "Batterier",
        }
        answers = {
            "reason": "replace",
            "works": "yes",
            "damage": "no",
            "age": "newer",
            "accessories": "complete",
            "safety": "risk",
        }

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "waste")
        self.assertEqual(result["decision_path"][0], "Mulig sikkerhedsrisiko")

    def test_comparables_require_relevant_title_tokens(self):
        page = """
        <a class="result__a" href="https://example.com/billy">IKEA BILLY reol brugt</a>
        <div>Pris 600 kr.</div>
        <a class="result__a" href="https://example.com/sofa">Tilfældig sofa</a>
        <div>Pris 8.000 kr.</div>
        """

        comparables = extract_comparables(page, "IKEA BILLY reol brugt pris Danmark")

        self.assertEqual(len(comparables), 1)
        self.assertEqual(comparables[0]["price"], 600)

    def test_manual_producer_and_model_are_added_to_context(self):
        assessment = {
            "object_name": "Cykel",
            "category": "Cykel og fritid",
            "waste_category": "Jern og metal eller storskrald",
            "materials": ["metal"],
            "brand": None,
            "model": None,
        }
        answers = {
            "producer": "other",
            "producer_name": "Kildemoes",
            "model_name": "Street 7",
        }

        context = build_context(assessment, answers)

        self.assertEqual(context["producer"], "kildemoes")
        self.assertEqual(context["model"], "street 7")
    def test_reshopper_only_relevant_for_matching_categories(self):
        self.assertTrue(
            is_reshopper_relevant({"object_name": "Barnevogn", "category": "Børn og baby"})
        )
        self.assertTrue(
            is_reshopper_relevant({"object_name": "Sofa", "category": "Møbler og bolig"})
        )
        self.assertFalse(
            is_reshopper_relevant({"object_name": "Cykel", "category": "Cykel og fritid"})
        )
        self.assertFalse(
            is_reshopper_relevant({"object_name": "Akkuboremaskine", "category": "Elektronik og værktøj"})
        )
    def test_known_working_bicycle_gets_realistic_price_floor(self):
        answers = {
            "works": "yes",
            "damage": "minor",
            "accessories": "complete",
            "producer_name": "Kildemoes",
            "model_name": "Street 7",
        }

        estimate = estimate_bicycle_price(answers, [])

        self.assertEqual(estimate["label"], "Sæt prisen til 2250 kr.")
        self.assertIn("Cykelestimat", estimate["note"])
    def test_sale_query_uses_selected_producer_and_manual_model(self):
        assessment = {
            "object_name": "Reol",
            "category": "Møbler og indbo",
            "subcategory": "Reol",
            "brand": None,
            "model": None,
        }
        answers = {
            "producer": "ikea",
            "model_name": "Billy",
        }

        query = build_sale_query(assessment, answers)

        self.assertEqual(query, "IKEA Billy Reol brugt pris Danmark")

    def test_sale_name_does_not_repeat_model_inside_object_name(self):
        from server import build_sale_object_name

        assessment = {
            "object_name": "BILLY-reol",
            "brand": "IKEA",
            "model": "BILLY",
        }

        name = build_sale_object_name(assessment, {"producer": "ikea"})

        self.assertEqual(name, "IKEA BILLY-reol")
    def test_premium_bicycle_model_gets_high_price_floor(self):
        answers = {
            "works": "yes",
            "damage": "minor",
            "accessories": "complete",
            "producer_name": "trek mardone slr 7 etep",
            "model_name": "",
        }

        estimate = estimate_bicycle_price(answers, [])

        self.assertEqual(estimate["label"], "Sæt prisen til 34500 kr.")
        self.assertIn("Premium-racercykel", estimate["note"])

    def test_price_extraction_understands_tusind_prices(self):
        prices = extract_prices("Prisen er 27tusind. Nypris 82.000 kr.")

        self.assertIn(27000, prices)

    def test_too_small_web_prices_fall_back_without_crashing(self):
        from server import estimate_sale_price

        estimate = estimate_sale_price(
            {"object_name": "Reol", "category": "Møbler"},
            {"works": "yes", "damage": "no", "age": "mid"},
            [25],
        )

        self.assertIn("prototypeestimat", estimate["note"])
    def test_sale_query_normalizes_premium_bicycle_typos(self):
        assessment = {
            "object_name": "Cykel",
            "category": "Cykel og fritid",
            "subcategory": "Cykel",
            "brand": None,
            "model": None,
        }
        answers = {
            "producer": "other",
            "producer_name": "trek mardone slr 7 etep",
            "model_name": "",
        }

        query = build_sale_query(assessment, answers)

        self.assertEqual(query, "Trek Madone SLR 7 eTap Cykel brugt pris Danmark")

if __name__ == "__main__":
    unittest.main()














