import unittest

from decision_engine import build_recommendation


class DecisionEngineTest(unittest.TestCase):
    def test_no_longer_used_working_item_should_be_sold_first(self):
        assessment = {
            "object_name": "Kontorstol",
            "category": "Moebler og indbo",
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
            ["Bruger den ikke laengere", "Vurder salg", "Tjek producentordninger", "Saelg"],
        )

    def test_defective_electronics_should_prioritize_repair(self):
        assessment = {
            "object_name": "Akkuboremaskine",
            "category": "Elektronik og vaerktoej",
            "waste_category": "Smaat elektronik",
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
        self.assertIn("Kontroller reparation", result["decision_path"])

    def test_user_thinks_waste_but_working_item_should_not_go_to_waste(self):
        assessment = {
            "object_name": "Kaffemaskine",
            "category": "Elektronik",
            "waste_category": "Smaat elektronik",
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
        self.assertEqual(result["decision_path"][1], "Kontroller salg")

    def test_heavily_damaged_non_electronics_can_end_as_waste(self):
        assessment = {
            "object_name": "Reol",
            "category": "Moebler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["trae"],
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
        self.assertIn("Alle cirkulaere muligheder er nej", result["decision_path"])

    def test_ikea_resale_is_a_producer_program_not_core_sell_logic(self):
        assessment = {
            "object_name": "BILLY bogreol",
            "category": "Moebler og indbo",
            "brand": "IKEA",
            "waste_category": "Storskrald eller genbrugsplads",
            "materials": ["trae"],
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

        self.assertEqual(result["recommended_action"], "sell")
        self.assertEqual(result["producer_program"]["status"], "likely")
        self.assertEqual(result["producer_program"]["programs"][0]["id"], "ikea_gensalg")


if __name__ == "__main__":
    unittest.main()
