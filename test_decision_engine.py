import unittest

from decision_engine import build_recommendation, score_actions


class DecisionEngineTest(unittest.TestCase):
    def test_working_item_without_need_should_be_sold_first(self):
        assessment = {
            "object_name": "Kontorstol",
            "category": "Moebler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
        }
        answers = {"works": "yes", "reason": "no_need", "damage": "no"}

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "sell")
        self.assertEqual(result["options"][0]["key"], "sell")

    def test_defective_electronics_should_prioritize_repair(self):
        assessment = {
            "object_name": "Akkuboremaskine",
            "category": "Elektronik og vaerktoej",
            "waste_category": "Smaat elektronik",
        }
        answers = {"works": "no", "reason": "defect", "battery": "battery"}

        scores = score_actions(assessment, answers)

        self.assertGreater(scores["repair"], scores["waste"])
        self.assertGreater(scores["repair"], scores["sell"])

    def test_heavily_damaged_non_electronics_can_end_as_waste(self):
        assessment = {
            "object_name": "Reol",
            "category": "Moebler og indbo",
            "waste_category": "Storskrald eller genbrugsplads",
        }
        answers = {"works": "no", "reason": "worn", "damage": "major"}

        result = build_recommendation(assessment, answers)

        self.assertEqual(result["recommended_action"], "waste")


if __name__ == "__main__":
    unittest.main()
