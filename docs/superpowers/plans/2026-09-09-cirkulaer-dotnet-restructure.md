# Cirkulær assistent .NET Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port Cirkulaer's Python backend to .NET (Aspire + Api + App) and deploy it to Azure Container Apps via azd and GitHub Actions, preserving the existing behaviour exactly.

**Architecture:** Two ASP.NET Core services orchestrated by .NET Aspire. `Api` holds the ported logic — vision assessment via OpenAI/Anthropic/Gemini, the pure decision tree, producer programs, and sale assist. `App` serves the existing vanilla-JS frontend from `wwwroot/` plus a `/config` endpoint. No database; producer programs live in JSON. The Python stays in `legacy/` as a reference implementation until golden-file parity is proven, then is deleted.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, .NET Aspire, xUnit, Azure Container Apps, `azd`, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-09-cirkulaer-dotnet-restructure-design.md`

## Global Constraints

- Target framework is `net10.0` for every project.
- Danish user-facing strings are **product copy** — copy them byte-for-byte from the Python. Never paraphrase, never translate, never "fix" spelling.
- Tri-state answers stay as `string?` holding the exact Python literals (`"yes"`, `"no"`, `"partly"`, `"unknown"`, `null`). Do not convert to enums.
- Error responses keep the exact shape `{"error": "..."}` — `app.js` parses it. 400 for validation, 500 otherwise.
- The reference Python lives in `legacy/` from Task 1 onward. When a task says "port `X` from `server.py:N`", read `legacy/server.py` at that line.
- Every ported function keeps its Python behaviour including edge cases. When Python and idiomatic C# disagree, Python wins.
- Use primary constructors where they fit. No comments unless the *why* is non-obvious. No docstrings.
- Run `dotnet build` after edits before reporting a task done.
- 1-based/0-based indexing, dictionary ordering and integer division must match Python. `len(filtered) // 2` is integer division.

---

### Task 1: Repository restructure and solution scaffold

**Files:**
- Create: `src/Cirkulaer.sln`, `src/Api/Api.csproj`, `src/App/App.csproj`, `src/Api.Tests/Api.Tests.csproj`, `src/Cirkulaer.AppHost/Cirkulaer.AppHost.csproj`, `src/Cirkulaer.ServiceDefaults/Cirkulaer.ServiceDefaults.csproj`
- Create: `.gitignore`
- Move: `server.py`, `decision_engine.py`, `producer_programs.py`, `test_decision_engine.py` → `legacy/`
- Move: `index.html`, `app.js`, `styles.css` → `src/App/wwwroot/`

**Interfaces:**
- Consumes: nothing
- Produces: a building solution at `src/Cirkulaer.sln` with five projects. Later tasks add files to `src/Api/`, `src/App/`, `src/Api.Tests/`.

- [ ] **Step 1: Move the Python to `legacy/` and the frontend to `wwwroot/`**

```bash
mkdir -p legacy src/App/wwwroot
git mv server.py decision_engine.py producer_programs.py test_decision_engine.py legacy/
git mv index.html app.js styles.css src/App/wwwroot/
```

- [ ] **Step 2: Verify the Python still runs from its new home**

```bash
cd legacy && python -m unittest test_decision_engine -v
```

Expected: all tests PASS. They import `decision_engine` and `server` as siblings, so the move is transparent. If they fail, stop — something else was assumed about the working directory.

- [ ] **Step 3: Create the projects and solution**

```bash
cd src
dotnet new sln -n Cirkulaer
dotnet new web -n App -f net10.0
dotnet new web -n Api -f net10.0
dotnet new xunit -n Api.Tests -f net10.0
dotnet new aspire-apphost -n Cirkulaer.AppHost -f net10.0
dotnet new aspire-servicedefaults -n Cirkulaer.ServiceDefaults -f net10.0
dotnet sln add App Api Api.Tests Cirkulaer.AppHost Cirkulaer.ServiceDefaults
dotnet add Api.Tests reference Api
dotnet add Api reference Cirkulaer.ServiceDefaults
dotnet add App reference Cirkulaer.ServiceDefaults
dotnet add Cirkulaer.AppHost reference Api
dotnet add Cirkulaer.AppHost reference App
```

The `dotnet new web` template creates `wwwroot/` only if missing — the frontend files moved in Step 1 stay put.

- [ ] **Step 4: Add `EnableSdkContainerSupport` to Api and App**

In both `src/Api/Api.csproj` and `src/App/App.csproj`, inside the existing `<PropertyGroup>`:

```xml
<EnableSdkContainerSupport>true</EnableSdkContainerSupport>
```

- [ ] **Step 5: Write `.gitignore`**

Create `.gitignore` at the repo root:

```gitignore
bin/
obj/
.vs/
*.user
.azure/
__pycache__/
*.pyc
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Cirkulaer.sln`
Expected: build succeeds, 5 projects.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: scaffold .NET solution, move python to legacy/"
```

---

### Task 2: Golden-file fixture harness

**Files:**
- Create: `scripts/dump-legacy-fixtures.py`
- Create: `src/Api.Tests/fixtures/decision-cases.json` (generated)
- Create: `src/Api.Tests/fixtures/sale-cases.json` (generated)

**Interfaces:**
- Consumes: `legacy/decision_engine.py`, `legacy/server.py`
- Produces: two JSON fixture files. Each is a list of `{"name", "assessment", "answers", "expected"}` objects. Tasks 3-6 and 10-13 assert their C# output equals `expected`.

**Why this task exists:** the decision tree has ~30 branches and the estimator has three price regimes. The ported unit tests alone are thin cover; these fixtures pin the actual output of the implementation being replaced.

- [ ] **Step 1: Write the fixture dumper**

Create `scripts/dump-legacy-fixtures.py`:

```python
import itertools
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "legacy"))

from decision_engine import build_recommendation
from server import build_sale_assist

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


def answer_grid():
    seen = set()
    for works, reason in itertools.product(WORKS, REASONS):
        for damage, age in itertools.product(DAMAGE, AGE):
            for accessories, safety in zip(ACCESSORIES, SAFETY):
                key = (works, reason, damage, age, accessories, safety)
                if key in seen:
                    continue
                seen.add(key)
                answers = {"works": works, "reason": reason, "damage": damage, "age": age,
                           "accessories": accessories, "safety": safety, "producer": "detected"}
                yield {k: v for k, v in answers.items() if v is not None}


def main():
    out_dir = os.path.join(os.path.dirname(__file__), "..", "src", "Api.Tests", "fixtures")
    os.makedirs(out_dir, exist_ok=True)

    decision_cases = []
    for a_index, assessment in enumerate(ASSESSMENTS):
        for c_index, answers in enumerate(answer_grid()):
            decision_cases.append({
                "name": f"a{a_index}-c{c_index}",
                "assessment": assessment,
                "answers": answers,
                "expected": build_recommendation(assessment, answers),
            })

    with open(os.path.join(out_dir, "decision-cases.json"), "w", encoding="utf-8") as handle:
        json.dump(decision_cases, handle, ensure_ascii=False, indent=2)

    # Sale assist reaches the network. Only the deterministic parts are pinned here:
    # the recorded run must be done with the network available, and the search note
    # is stripped so the fixture does not depend on live search results.
    sale_cases = []
    for a_index, assessment in enumerate(ASSESSMENTS):
        for c_index, answers in enumerate(itertools.islice(answer_grid(), 40)):
            recommendation = build_recommendation(assessment, answers)
            sale = build_sale_assist(assessment, answers, recommendation)
            for volatile in ("price", "price_note", "search_note", "search_url",
                             "signals", "comparables", "price_confidence"):
                sale.pop(volatile, None)
            sale_cases.append({
                "name": f"a{a_index}-c{c_index}",
                "assessment": assessment,
                "answers": answers,
                "expected": sale,
            })

    with open(os.path.join(out_dir, "sale-cases.json"), "w", encoding="utf-8") as handle:
        json.dump(sale_cases, handle, ensure_ascii=False, indent=2)

    print(f"decision cases: {len(decision_cases)}")
    print(f"sale cases: {len(sale_cases)}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Generate the fixtures**

```bash
python scripts/dump-legacy-fixtures.py
```

Expected: prints two non-zero counts, writes both JSON files. If `build_sale_assist` raises because the network is unavailable, note that its `except Exception` fallback still returns a dict — the run should not crash.

- [ ] **Step 3: Sanity-check a fixture by hand**

```bash
python -c "import json;d=json.load(open('src/Api.Tests/fixtures/decision-cases.json',encoding='utf-8'));print(len(d));print(json.dumps(d[0],ensure_ascii=False,indent=2)[:600])"
```

Expected: a case with `recommended_action`, `decision_path`, `confidence`, `reasoning`, `checks`, `options`, `impact`, `producer_program`, `waste`.

- [ ] **Step 4: Mark the fixtures as generated**

Add to `.gitignore` — nothing. These are committed deliberately: they are the contract the port is verified against.

- [ ] **Step 5: Commit**

```bash
git add scripts/dump-legacy-fixtures.py src/Api.Tests/fixtures
git commit -m "test: add golden-file fixtures dumped from legacy python"
```

---

### Task 3: `Normalize` and the shared text helpers

**Files:**
- Create: `src/Api/Decision/TextHelpers.cs`
- Test: `src/Api.Tests/TextHelpersTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `public static class TextHelpers` with `public static string Normalize(string? value)` and `public static string CanonicalCategoryId(string? category)`. Used by Tasks 4, 5, 6, 10.

- [ ] **Step 1: Write the failing test**

Create `src/Api.Tests/TextHelpersTests.cs`:

```csharp
using Api.Decision;

namespace Api.Tests;

public class TextHelpersTests
{
    [Theory]
    [InlineData("Møbler og indbo", "moebler og indbo")]
    [InlineData("Elektronik og værktøj", "elektronik og vaerktoej")]
    [InlineData("Blå", "blaa")]
    [InlineData("ÆØÅ", "aeoeaa")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_matches_python(string? input, string expected)
        => Assert.Equal(expected, TextHelpers.Normalize(input));

    [Theory]
    [InlineData("Møbler og indbo", "furniture")]
    [InlineData("Elektronik og værktøj", "electronics")]
    public void CanonicalCategoryId_maps_plural_danish(string input, string expected)
        => Assert.Equal(expected, TextHelpers.CanonicalCategoryId(input));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter TextHelpersTests`
Expected: FAIL — `Api.Decision` does not exist.

- [ ] **Step 3: Implement**

Port `normalize` from `legacy/decision_engine.py:405` and `canonical_category_id` from `legacy/server.py:391`. Read both before writing. `normalize` lowercases then replaces `æ→ae`, `ø→oe`, `å→aa`, in that order. Create `src/Api/Decision/TextHelpers.cs`:

```csharp
namespace Api.Decision;

public static class TextHelpers
{
    public static string Normalize(string? value) =>
        (value ?? "").ToLowerInvariant()
            .Replace("æ", "ae")
            .Replace("ø", "oe")
            .Replace("å", "aa");

    public static string CanonicalCategoryId(string? category)
    {
        var value = (category ?? "").ToLowerInvariant();
        if (value.Contains("elektronik") || value.Contains("værktøj") || value.Contains("vaerktoej"))
            return "electronics";
        if (value.Contains("møbl") || value.Contains("moebl") || value.Contains("mobl"))
            return "furniture";
        if (value.Contains("cykel") || value.Contains("bike"))
            return "bicycle";
        if (value.Contains("tekstil") || value.Contains("tøj") || value.Contains("toej"))
            return "textile";
        if (value.Contains("farligt") || value.Contains("kemi"))
            return "hazardous";
        return "other";
    }
}
```

Note the order matters and the first match wins — `"Elektronik og værktøj"` must hit `electronics` before any later branch. Note also that `CanonicalCategoryId` matches on **raw lowercased** text (it lists `"møbl"` and `"moebl"` separately), while `Normalize` transliterates. They are different operations; do not merge them.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Api.Tests --filter TextHelpersTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Decision/TextHelpers.cs src/Api.Tests/TextHelpersTests.cs
git commit -m "feat(decision): port normalize and canonical_category_id"
```

---

### Task 4: Decision models and `BuildContext`

**Files:**
- Create: `src/Api/Decision/Models.cs`
- Create: `src/Api/Decision/DecisionEngine.cs` (partial — `BuildContext` only)
- Test: `src/Api.Tests/BuildContextTests.cs`

**Interfaces:**
- Consumes: `TextHelpers.Normalize` (Task 3)
- Produces:
  - `public sealed record Assessment` with properties `ObjectName`, `Category`, `CategoryId`, `Subcategory`, `Brand`, `Model`, `Materials` (`IReadOnlyList<string>`), `VisibleDamage` (`IReadOnlyList<string>`), `ConditionEstimate`, `Confidence` (`double?`), `WasteCategory`, `UncertaintyNotes` (`IReadOnlyList<string>`), all `string?` unless noted, all JSON-mapped to the Python snake_case names.
  - `public sealed record DecisionContext` with the 24 fields `build_context` returns.
  - `public static DecisionContext BuildContext(Assessment assessment, IReadOnlyDictionary<string, string?> answers)`

**Note on `answers`:** the Python passes a free-form dict. Keep it as `IReadOnlyDictionary<string, string?>` so absent-vs-`"unknown"` stays distinguishable. Do not introduce an `Answers` record.

- [ ] **Step 1: Write the failing test**

Create `src/Api.Tests/BuildContextTests.cs`:

```csharp
using Api.Decision;

namespace Api.Tests;

public class BuildContextTests
{
    static Assessment Chair() => new()
    {
        ObjectName = "Kontorstol",
        Category = "Møbler og indbo",
        Materials = ["metal", "tekstil"],
        Brand = "IKEA",
        Model = "MARKUS",
    };

    [Fact]
    public void Detected_producer_falls_back_to_assessment_brand()
    {
        var context = DecisionEngine.BuildContext(Chair(),
            new Dictionary<string, string?> { ["producer"] = "detected" });

        Assert.Equal("ikea", context.Producer);
    }

    [Fact]
    public void Manual_producer_and_model_are_used()
    {
        var context = DecisionEngine.BuildContext(Chair(),
            new Dictionary<string, string?>
            {
                ["producer"] = "other",
                ["producer_name"] = "Håg",
                ["model_name"] = "Capisco",
            });

        Assert.Equal("haag", context.Producer);
        Assert.Equal("capisco", context.Model);
    }

    [Fact]
    public void Unknown_producer_clears_it()
    {
        var context = DecisionEngine.BuildContext(Chair(),
            new Dictionary<string, string?> { ["producer"] = "unknown" });

        Assert.Equal("", context.Producer);
    }

    [Fact]
    public void Furniture_is_detected_from_normalized_category()
    {
        var context = DecisionEngine.BuildContext(Chair(), new Dictionary<string, string?>());

        Assert.True(context.IsFurniture);
        Assert.False(context.IsElectronics);
    }

    [Fact]
    public void Battery_material_marks_electronics_and_battery()
    {
        var assessment = Chair() with { Materials = ["plast", "batteri"] };

        var context = DecisionEngine.BuildContext(assessment, new Dictionary<string, string?>());

        Assert.True(context.HasBattery);
        Assert.True(context.IsElectronics);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter BuildContextTests`
Expected: FAIL — `Assessment` and `DecisionEngine` do not exist.

- [ ] **Step 3: Implement**

Port `build_context` from `legacy/decision_engine.py:86-135` and `infer_damage_from_assessment` from `:138-151`. Read both first.

Watch these specifics:
- `selected_producer in (None, "", "detected")` → use the assessment brand.
- `"other"` **with** a non-empty `producer_name` → use `producer_name`. `"other"` without one falls through to the final `else`, which uses the literal `"other"`.
- `condition` is `answers["condition"]` or the assessment's `condition_estimate`, then `or "unknown"`.
- `damage` is `answers["damage"]` or `InferDamageFromAssessment(assessment)`.
- `is_electronics` is true if `category_id == "electronics"` **or** `"elektronik"` is in the normalized category **or** `"batteri"` is in the joined materials.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Api.Tests --filter BuildContextTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Decision src/Api.Tests/BuildContextTests.cs
git commit -m "feat(decision): port assessment models and build_context"
```

---

### Task 5: Decision tree — possibilities, action choice, and recommendation

**Files:**
- Modify: `src/Api/Decision/DecisionEngine.cs`
- Modify: `src/Api/Decision/Models.cs`
- Test: `src/Api.Tests/DecisionEngineTests.cs`
- Test: `src/Api.Tests/DecisionParityTests.cs`

**Interfaces:**
- Consumes: `DecisionEngine.BuildContext`, `DecisionContext`, `Assessment` (Task 4); `ProducerPrograms.Evaluate` is added in Task 6 — until then `BuildRecommendation` sets `ProducerProgram = null`.
- Produces: `public static Recommendation BuildRecommendation(Assessment assessment, IReadOnlyDictionary<string, string?> answers)` and the `Recommendation` record matching the Python response keys.

- [ ] **Step 1: Write the failing behaviour tests**

Create `src/Api.Tests/DecisionEngineTests.cs`, porting the decision-tree cases from `legacy/test_decision_engine.py`. Port at minimum these, keeping the Danish assertions verbatim:

```csharp
using Api.Decision;

namespace Api.Tests;

public class DecisionEngineTests
{
    [Fact]
    public void No_longer_used_working_item_is_sold_first()
    {
        var assessment = new Assessment
        {
            ObjectName = "Kontorstol",
            Category = "Møbler og indbo",
            WasteCategory = "Storskrald eller genbrugsplads",
            Materials = ["metal", "tekstil"],
        };
        var answers = new Dictionary<string, string?>
        {
            ["works"] = "yes", ["reason"] = "no_need", ["damage"] = "no",
            ["age"] = "mid", ["accessories"] = "complete", ["producer"] = "other",
        };

        var result = DecisionEngine.BuildRecommendation(assessment, answers);

        Assert.Equal("sell", result.RecommendedAction);
        Assert.Equal(
            ["Bruger den ikke længere", "Vurder salg", "Tjek producentordninger", "Sælg"],
            result.DecisionPath);
    }

    [Fact]
    public void Safety_risk_overrides_every_circular_action()
    {
        var assessment = new Assessment { ObjectName = "Elvarmer", Category = "Elektronik" };
        var answers = new Dictionary<string, string?>
        {
            ["works"] = "yes", ["reason"] = "no_need", ["safety"] = "risk",
        };

        var result = DecisionEngine.BuildRecommendation(assessment, answers);

        Assert.Equal("waste", result.RecommendedAction);
        Assert.Equal(
            ["Mulig sikkerhedsrisiko", "Undgå videre brug", "Sikker aflevering"],
            result.DecisionPath);
    }
}
```

Also port: `test_defective_electronics_should_prioritize_repair`, `test_user_thinks_waste_but_working_item_should_not_go_to_waste`, `test_heavily_damaged_non_electronics_can_end_as_waste`, `test_defective_item_without_realistic_repair_becomes_waste`, `test_furniture_can_prioritize_cleaning_before_sale`, `test_visible_ai_damage_is_used_when_user_has_not_answered`. Read each from `legacy/test_decision_engine.py` and translate the assertions literally.

- [ ] **Step 2: Write the parity test**

Create `src/Api.Tests/DecisionParityTests.cs`:

```csharp
using System.Text.Json;
using Api.Decision;

namespace Api.Tests;

public class DecisionParityTests
{
    public record Case(string Name, Assessment Assessment,
                       Dictionary<string, string?> Answers, JsonElement Expected);

    public static TheoryData<Case> Cases()
    {
        var json = File.ReadAllText("fixtures/decision-cases.json");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var data = new TheoryData<Case>();
        foreach (var item in JsonSerializer.Deserialize<List<Case>>(json, options)!)
            data.Add(item);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Csharp_output_matches_python(Case testCase)
    {
        var actual = DecisionEngine.BuildRecommendation(testCase.Assessment, testCase.Answers);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var actualJson = JsonSerializer.SerializeToElement(actual, options);

        Assert.Equal(
            JsonSerializer.Serialize(testCase.Expected, options),
            JsonSerializer.Serialize(actualJson, options));
    }
}
```

Add to `src/Api.Tests/Api.Tests.csproj` so the fixtures reach the output directory:

```xml
<ItemGroup>
  <Content Include="fixtures\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Note: the parity test will fail on `producer_program` until Task 6 lands. Mark it `Skip = "producer programs land in Task 6"` now and remove the skip in Task 6 Step 5.

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test src/Api.Tests --filter DecisionEngineTests`
Expected: FAIL — `BuildRecommendation` does not exist.

- [ ] **Step 4: Implement**

Port from `legacy/decision_engine.py`, in this order: `ACTION_TEXT` and `REASON_LABELS` (`:4-50`), `evaluate_possibilities` (`:154-232`), `choose_action` (`:235-290`), `prefer_clean_sell_or_donate` (`:293-301`), `fallback_after_repair` (`:304-311`), `order_actions` (`:314-326`), `build_reasoning` (`:329-360`), `build_checks` (`:363-373`), `build_impact` (`:376-389`), `confidence_label` (`:392-402`), and finally `build_recommendation` (`:53-84`).

Specifics that will bite:
- `order_actions` sorts by `(-score, circular_order.index(action))`. Use `OrderByDescending(score).ThenBy(circularOrderIndex)`.
- `confidence_label` treats `None`, `""` **and** `"unknown"` as missing.
- `waste` is realistic when `safety_risk` **or** nothing else is realistic.
- `build_checks` marks the recommended action `"realistisk"` even if its possibility is false.
- In `build_recommendation`, `producer_program` comes from `evaluate_producer_program(context)` — leave it `null` until Task 6.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test src/Api.Tests --filter DecisionEngineTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Decision src/Api.Tests
git commit -m "feat(decision): port decision tree and recommendation builder"
```

---

### Task 6: Producer programs

**Files:**
- Create: `src/Api/Producers/ProducerPrograms.cs`
- Create: `src/Api/Producers/ProducerProgram.cs`
- Create: `src/Api/data/producer-programs.json`
- Modify: `src/Api/Decision/DecisionEngine.cs` (wire `ProducerProgram` into `BuildRecommendation`)
- Modify: `src/Api.Tests/DecisionParityTests.cs` (remove the skip)
- Test: `src/Api.Tests/ProducerProgramsTests.cs`

**Interfaces:**
- Consumes: `DecisionContext` (Task 4), `TextHelpers.Normalize` (Task 3)
- Produces: `public static class ProducerPrograms` with `Evaluate(DecisionContext context)` returning `ProducerProgramResult?`, and `FindCandidates(Assessment assessment)`.

- [ ] **Step 1: Extract the data to JSON**

Convert `PRODUCER_PROGRAMS` from `legacy/producer_programs.py:1-64` into `src/Api/data/producer-programs.json` as a JSON array. Keep every field and every hint string exactly, including the normalized Danish spellings (`skaenk`, `porcelaen`, `boernemoebel`). Generate it rather than retyping:

```bash
python -c "
import sys, json
sys.path.insert(0, 'legacy')
from producer_programs import PRODUCER_PROGRAMS
print(json.dumps(PRODUCER_PROGRAMS, ensure_ascii=False, indent=2))
" > src/Api/data/producer-programs.json
```

Add to `src/Api/Api.csproj`:

```xml
<ItemGroup>
  <Content Include="data\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `src/Api.Tests/ProducerProgramsTests.cs`, porting `test_ikea_resale_is_a_producer_program_not_core_sell_logic` and `test_ikea_program_stays_possible_until_requirements_are_confirmed` from `legacy/test_decision_engine.py:166-215`. Read them first and translate the assertions literally.

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter ProducerProgramsTests`
Expected: FAIL — `Api.Producers` does not exist.

- [ ] **Step 4: Implement**

Port `find_program_candidates` (`:65-83`), `find_producer_programs` (`:84-91`), `evaluate_producer_program` (`:92-116`), `evaluate_single_program` (`:117-193`), `tri_state` (`:194-201`) and `normalize` (`:202`) from `legacy/producer_programs.py`. Load the JSON once into a static readonly list.

`tri_state` returns three states, not a bool — a check is `"ja"`, `"nej"` or `"ukendt"`, and a program stays "possible" while any check is unknown. That distinction is what the second test pins.

- [ ] **Step 5: Wire into `BuildRecommendation` and un-skip parity**

In `DecisionEngine.BuildRecommendation`, set `ProducerProgram = ProducerPrograms.Evaluate(context)`. Remove the `Skip` argument from `DecisionParityTests`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test src/Api.Tests`
Expected: PASS, including every `DecisionParityTests` case. A parity failure here is a real port bug — read the diff, fix the C#, do not edit the fixture.

- [ ] **Step 7: Commit**

```bash
git add src/Api/Producers src/Api/data src/Api/Decision src/Api.Tests
git commit -m "feat(producers): port producer programs, decision parity green"
```

---

### Task 7: Assessment schema, normalisation, and test assessments

**Files:**
- Create: `src/Api/Ai/AssessmentSchema.cs`
- Create: `src/Api/Ai/AssessmentNormalizer.cs`
- Create: `src/Api/Ai/TestAssessments.cs`
- Test: `src/Api.Tests/AssessmentNormalizerTests.cs`

**Interfaces:**
- Consumes: `Assessment` (Task 4), `TextHelpers.CanonicalCategoryId` (Task 3)
- Produces: `AssessmentSchema.Json` (the strict JSON schema as a `string`), `AssessmentNormalizer.Normalize(JsonElement raw) -> Assessment`, `TestAssessments.Build(string filename) -> Assessment`.

- [ ] **Step 1: Write the failing test**

Create `src/Api.Tests/AssessmentNormalizerTests.cs`, porting `test_billy_scenario_identifies_ikea_brand_and_model` from `legacy/test_decision_engine.py:19-25`:

```csharp
using Api.Ai;

namespace Api.Tests;

public class AssessmentNormalizerTests
{
    [Fact]
    public void Billy_fixture_identifies_ikea_brand_and_model()
    {
        var assessment = TestAssessments.Build("billy");

        Assert.Equal("BILLY-reol", assessment.ObjectName);
        Assert.Equal("IKEA", assessment.Brand);
        Assert.Equal("BILLY", assessment.Model);
        Assert.Equal("Bogreol", assessment.Subcategory);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter AssessmentNormalizerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

- `AssessmentSchema.Json`: copy `ASSESSMENT_SCHEMA` from `legacy/server.py:22-59` verbatim as a raw string literal (`"""`). It is sent to OpenAI as `text.format.schema` with `strict: true`, so every property must stay in `required` and `additionalProperties` must stay `false`.
- `AssessmentNormalizer.Normalize`: port `normalize_assessment` from `legacy/server.py:360-390`. It coerces missing fields to defaults and clamps `confidence`.
- `TestAssessments.Build`: port `build_test_assessment` from `legacy/server.py:184-284`. It matches on filename substrings and returns canned assessments.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Api.Tests --filter AssessmentNormalizerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Ai src/Api.Tests/AssessmentNormalizerTests.cs
git commit -m "feat(ai): port assessment schema, normalizer and test fixtures"
```

---

### Task 8: Vision providers

**Files:**
- Create: `src/Api/Ai/IVisionProvider.cs`
- Create: `src/Api/Ai/OpenAiVisionProvider.cs`
- Create: `src/Api/Ai/AnthropicVisionProvider.cs`
- Create: `src/Api/Ai/GeminiVisionProvider.cs`
- Create: `src/Api/Ai/LocalTestProvider.cs`
- Create: `src/Api/Ai/VisionProviderFactory.cs`
- Test: `src/Api.Tests/VisionResponseParsingTests.cs`
- Test fixture: `src/Api.Tests/fixtures/openai-response.json`

**Interfaces:**
- Consumes: `AssessmentSchema`, `AssessmentNormalizer`, `TestAssessments` (Task 7)
- Produces:
  - `public interface IVisionProvider { string Name { get; } Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct); }`
  - `public sealed class VisionProviderFactory` with `IVisionProvider Resolve(string? requested)` — picks by request field, falls back to `Ai:DefaultProvider`, falls back to `LocalTestProvider` when no key is configured.

- [ ] **Step 1: Capture a response fixture**

Take the shape from `legacy/server.py:339-348` (`extract_response_text` walks `output[].content[].type == "output_text"`). Write `src/Api.Tests/fixtures/openai-response.json` by hand:

```json
{
  "output": [
    {
      "content": [
        {
          "type": "output_text",
          "text": "{\"object_name\":\"BILLY-reol\",\"category\":\"Møbler og indbo\",\"category_id\":\"furniture\",\"subcategory\":\"Bogreol\",\"brand\":\"IKEA\",\"model\":\"BILLY\",\"materials\":[\"spånplade\"],\"visible_damage\":[],\"condition_estimate\":\"good\",\"confidence\":0.88,\"waste_category\":\"Storskrald\",\"uncertainty_notes\":[]}"
        }
      ]
    }
  ]
}
```

- [ ] **Step 2: Write the failing test**

Create `src/Api.Tests/VisionResponseParsingTests.cs`:

```csharp
using System.Text.Json;
using Api.Ai;

namespace Api.Tests;

public class VisionResponseParsingTests
{
    [Fact]
    public void Openai_response_is_parsed_into_an_assessment()
    {
        var json = File.ReadAllText("fixtures/openai-response.json");

        var assessment = OpenAiVisionProvider.ParseResponse(JsonDocument.Parse(json).RootElement);

        Assert.Equal("BILLY-reol", assessment.ObjectName);
        Assert.Equal("furniture", assessment.CategoryId);
        Assert.Equal(0.88, assessment.Confidence);
    }

    [Fact]
    public void Text_wrapped_in_prose_still_parses()
    {
        var element = JsonDocument.Parse(
            """{"output":[{"content":[{"type":"output_text","text":"Her er svaret: {\"object_name\":\"Stol\"} tak"}]}]}""")
            .RootElement;

        var assessment = OpenAiVisionProvider.ParseResponse(element);

        Assert.Equal("Stol", assessment.ObjectName);
    }
}
```

The second test pins `parse_json_object` (`legacy/server.py:350-358`), which falls back to a `\{.*\}` regex when the text is not clean JSON.

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter VisionResponseParsingTests`
Expected: FAIL.

- [ ] **Step 4: Implement the OpenAI provider**

Port `analyze_with_openai` (`legacy/server.py:285-337`), `extract_response_text` (`:339-348`) and `parse_json_object` (`:350-358`). Expose `ParseResponse(JsonElement)` as a `public static` method so it is testable without a network call. Body shape: `model`, `input` with one user message containing `input_text` then up to four `input_image` entries, and `text.format` = `{type: "json_schema", name: "circular_object_assessment", strict: true, schema: ...}`. POST to `https://api.openai.com/v1/responses` with `Authorization: Bearer`. Use `IHttpClientFactory`; keep the 60-second timeout from the Python.

- [ ] **Step 5: Implement Anthropic, Gemini and LocalTest**

Adapt the Anthropic and Gemini calls from `C:\Development\Affaldssortering\src\Api\Endpoints\ClassifyEndpoints.cs:480-510` — read that file for the exact request shapes and auth headers (Anthropic: `x-api-key` + `anthropic-version`; Gemini: `x-goog-api-key`). Both must return the same `Assessment` and honour `AssessmentSchema`. `LocalTestProvider` returns `TestAssessments.Build(filename)` and sets an `uncertainty_notes` entry marking it a test analysis, matching `legacy/server.py:142-150`.

- [ ] **Step 6: Run to verify tests pass**

Run: `dotnet test src/Api.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Api/Ai src/Api.Tests
git commit -m "feat(ai): port vision providers for openai, anthropic and gemini"
```

---

### Task 9: Sale query building

**Files:**
- Create: `src/Api/SaleAssist/SaleQueryBuilder.cs`
- Test: `src/Api.Tests/SaleQueryBuilderTests.cs`

**Interfaces:**
- Consumes: `Assessment` (Task 4)
- Produces: `public static class SaleQueryBuilder` with `BuildSaleQuery`, `NormalizeSearchTerms`, `CompactRedundantParts`, `ProducerSearchName`, `BuildMarketplaceSearchUrl`, `BuildReshopperUrl`, `IsReshopperRelevant`, `BuildReshopperNote`, `BuildSaleObjectName`, `BuildSaleDetails` — all `public static`, all matching the Python signatures.

- [ ] **Step 1: Write the failing tests**

Port from `legacy/test_decision_engine.py`: `test_detected_marker_is_not_written_into_sale_query` (`:229`), `test_sale_query_uses_selected_producer_and_manual_model` (`:334`), `test_sale_name_does_not_repeat_model_inside_object_name` (`:351`), `test_sale_query_normalizes_premium_bicycle_typos` (`:392`), `test_reshopper_only_relevant_for_matching_categories` (`:308`). Read each and translate literally. Example:

```csharp
using Api.SaleAssist;

namespace Api.Tests;

public class SaleQueryBuilderTests
{
    [Fact]
    public void Detected_marker_is_not_written_into_sale_query()
    {
        var assessment = new Assessment { ObjectName = "Kontorstol", Category = "Møbler og indbo" };
        var answers = new Dictionary<string, string?> { ["producer"] = "detected" };

        var query = SaleQueryBuilder.BuildSaleQuery(assessment, answers);

        Assert.DoesNotContain("detected", query);
    }

    [Fact]
    public void Premium_bicycle_typos_are_normalized()
    {
        var assessment = new Assessment { ObjectName = "Racercykel" };
        var answers = new Dictionary<string, string?>
        {
            ["producer_name"] = "trek", ["model_name"] = "mardone slr etep",
        };

        var query = SaleQueryBuilder.BuildSaleQuery(assessment, answers);

        Assert.Contains("Madone", query);
        Assert.Contains("SLR", query);
        Assert.Contains("eTap", query);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Api.Tests --filter SaleQueryBuilderTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

Port `legacy/server.py:436-569` — `build_sale_query`, `normalize_search_terms`, `compact_redundant_parts`, `producer_search_name`, `build_marketplace_search_url`, `build_reshopper_url`, `is_reshopper_relevant`, `build_reshopper_note`, `build_sale_object_name`, `build_sale_details`.

Specifics:
- `build_sale_query` deduplicates case-insensitively while preserving first-seen order, then drops parts contained in a longer part, then appends `" brugt pris Danmark"`.
- `compact_redundant_parts` uses the regex `(?<![a-z0-9]){escaped}(?![a-z0-9])` against the *lowercased* parts. Use `Regex.Escape` and the same lookarounds.
- `producer_search_name` excludes the literals `"unknown"`, `"other"`, `"detected"`, `"ved ikke"`, `"anden"`, and special-cases `"ikea"` → `"IKEA"`.
- `is_reshopper_relevant` matches raw lowercased text, **not** `Normalize` output — it lists both `"børn"` and `"boern"` spellings explicitly. Do not substitute `TextHelpers.Normalize` here.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/Api.Tests --filter SaleQueryBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Api/SaleAssist src/Api.Tests/SaleQueryBuilderTests.cs
git commit -m "feat(sale): port sale query and marketplace link building"
```

---

### Task 10: Price search client

**Files:**
- Create: `src/Api/SaleAssist/PriceSearchClient.cs`
- Create: `src/Api/SaleAssist/PriceSignals.cs`
- Test: `src/Api.Tests/PriceSearchClientTests.cs`
- Test fixture: `src/Api.Tests/fixtures/duckduckgo-page.html`

**Interfaces:**
- Consumes: nothing from earlier tasks
- Produces:
  - `public sealed record Comparable(string Title, int Price, double Relevance, string Url)` — matches the Python dict built at `legacy/server.py:638-645`; `Relevance` is rounded to 2 decimals there.
  - `public sealed record PriceSignals(string Url, IReadOnlyList<int> Prices, IReadOnlyList<string> Signals, IReadOnlyList<Comparable> Comparables, string Confidence, string Note)`   - `public interface IPriceSearch { Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct); }` — exists so Task 12's parity tests can substitute a stub.
  - `public sealed class PriceSearchClient(HttpClient http, ILogger<PriceSearchClient> logger) : IPriceSearch`. Parsing helpers `ExtractComparables`, `ExtractPrices`, `ComparableQueryTokens`, `CleanHtmlText`, `DecodeSearchResultUrl` are `public static` for testing.

**Note:** this is the only ported component that reaches the internet, and it is expected to fail from Azure. Its failure path is load-bearing — see the spec's "Price search: known limitation" section. Do not "improve" the fallback.

- [ ] **Step 1: Capture a page fixture**

```bash
curl -s -A "Mozilla/5.0 CirkulaerPrototype/1.0" \
  "https://duckduckgo.com/html/?q=IKEA+BILLY+reol+brugt+pris+Danmark" \
  -o src/Api.Tests/fixtures/duckduckgo-page.html
head -c 400 src/Api.Tests/fixtures/duckduckgo-page.html
```

If the response is a challenge page rather than results, that is itself the answer to the deployment question — record it, and hand-write a minimal fixture matching the markup `extract_comparables` expects (read `legacy/server.py:614-648` for the exact regexes).

- [ ] **Step 2: Write the failing tests**

Port `test_comparables_require_relevant_title_tokens` (`legacy/test_decision_engine.py:276`), `test_price_extraction_understands_tusind_prices` (`:377`) and `test_too_small_web_prices_fall_back_without_crashing` (`:382`):

```csharp
using Api.SaleAssist;

namespace Api.Tests;

public class PriceSearchClientTests
{
    [Fact]
    public void Extract_prices_understands_tusind_form()
    {
        var prices = PriceSearchClient.ExtractPrices("Sælges for 2 tusind kr.");

        Assert.Contains(2000, prices);
    }

    [Fact]
    public void Comparables_require_relevant_title_tokens()
    {
        var page = File.ReadAllText("fixtures/duckduckgo-page.html");

        var comparables = PriceSearchClient.ExtractComparables(page, "IKEA BILLY reol brugt pris Danmark");

        Assert.All(comparables, c => Assert.True(c.Price > 0));
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test src/Api.Tests --filter PriceSearchClientTests`
Expected: FAIL.

- [ ] **Step 4: Implement**

Port `legacy/server.py:570-697` — `search_price_signals`, `extract_comparables`, `comparable_query_tokens`, `clean_html_text`, `decode_search_result_url`, `extract_search_signals`, `extract_prices`.

Specifics:
- Keep the `User-Agent: Mozilla/5.0 CirkulaerPrototype/1.0` header and the 12-second timeout.
- The catch-all failure path returns `prices: []`, `confidence: "lav"`, the Google fallback URL and the exact Danish note from `:588`. Add `logger.LogWarning` there — this is the only addition to the ported behaviour, and it is what tells you whether Azure is blocked.
- Confidence thresholds: `>= 5` → `"høj"`, `>= 3` → `"middel"`, else `"lav"`.
- `comparables[:8]` and `signals` from `comparables[:5]`.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test src/Api.Tests --filter PriceSearchClientTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Api/SaleAssist src/Api.Tests
git commit -m "feat(sale): port duckduckgo price search with logged failure path"
```

---

### Task 11: Price estimation

**Files:**
- Create: `src/Api/SaleAssist/PriceEstimator.cs`
- Test: `src/Api.Tests/PriceEstimatorTests.cs`

**Interfaces:**
- Consumes: `Assessment` (Task 4), `SaleQueryBuilder.ProducerSearchName` (Task 9)
- Produces: `public static class PriceEstimator` with `PriceEstimate Estimate(Assessment assessment, IReadOnlyDictionary<string, string?> answers, IReadOnlyList<int> prices)` returning `public sealed record PriceEstimate(string Label, string Note)`. Helpers `EstimateBicyclePrice`, `IsPremiumBicycle`, `EstimatePremiumBicyclePrice`, `AgePriceFactor`, `DamagePriceFactor`, `TrimPriceOutliers`, `RoundToNearest25/50/500` all `public static`.

- [ ] **Step 1: Write the failing tests**

Port `test_known_working_bicycle_gets_realistic_price_floor` (`legacy/test_decision_engine.py:321`) and `test_premium_bicycle_model_gets_high_price_floor` (`:363`):

```csharp
using Api.SaleAssist;

namespace Api.Tests;

public class PriceEstimatorTests
{
    [Fact]
    public void Working_known_bicycle_gets_a_price_floor_without_web_prices()
    {
        var assessment = new Assessment { ObjectName = "Cykel", Category = "Cykler" };
        var answers = new Dictionary<string, string?>
        {
            ["works"] = "yes", ["damage"] = "no", ["accessories"] = "complete",
            ["producer_name"] = "Trek", ["age"] = "mid",
        };

        var estimate = PriceEstimator.Estimate(assessment, answers, []);

        Assert.Contains("kr.", estimate.Label);
        Assert.Contains("Cykelestimat", estimate.Note);
    }

    [Fact]
    public void Premium_racer_is_detected_from_producer_and_model()
    {
        var answers = new Dictionary<string, string?>
        {
            ["producer_name"] = "Trek", ["model_name"] = "Madone SLR eTap",
        };

        Assert.True(PriceEstimator.IsPremiumBicycle(answers));
    }

    [Theory]
    [InlineData("newer")]
    [InlineData("mid")]
    [InlineData("old")]
    [InlineData(null)]
    public void Age_factor_is_defined_for_every_age(string? age)
        => Assert.True(PriceEstimator.AgePriceFactor(age) > 0);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Api.Tests --filter PriceEstimatorTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

Port `legacy/server.py:698-876` — `estimate_sale_price`, `estimate_bicycle_price`, `is_premium_bicycle`, `estimate_premium_bicycle_price`, `round_to_nearest_500`, `age_price_factor`, `damage_price_factor`, `trim_price_outliers`, `round_to_nearest_25`, `round_to_nearest_50`.

Specifics:
- `filtered[len(filtered) // 2]` is integer division on a **sorted** list — check whether `trim_price_outliers` sorts, and match it.
- `is_premium_bicycle` requires a producer hit **and** a premium-term hit, after the `mardone→madone` and `etep→etap` replacements.
- The bicycle floor `max(midpoint, 1900 if known_model else 1500)` applies only when working, undamaged and complete.
- Rounding uses Python's `round()`, which is banker's rounding. `Math.Round(x)` in .NET is also banker's rounding by default — keep the default, do **not** pass `MidpointRounding.AwayFromZero`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/Api.Tests --filter PriceEstimatorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Api/SaleAssist/PriceEstimator.cs src/Api.Tests/PriceEstimatorTests.cs
git commit -m "feat(sale): port price estimation incl. bicycle and premium regimes"
```

---

### Task 12: Ad text and sale-assist composition

**Files:**
- Create: `src/Api/SaleAssist/AdTextBuilder.cs`
- Create: `src/Api/SaleAssist/SaleAssistBuilder.cs`
- Test: `src/Api.Tests/SaleAssistParityTests.cs`

**Interfaces:**
- Consumes: `SaleQueryBuilder` (Task 9), `PriceSearchClient` (Task 10), `PriceEstimator` (Task 11)
- Produces: `public sealed class SaleAssistBuilder(IPriceSearch search)` with `Task<SaleAssistResult> BuildAsync(Assessment assessment, IReadOnlyDictionary<string, string?> answers, Recommendation recommendation, CancellationToken ct)`, and the `SaleAssistResult` record matching the Python response keys exactly.

- [ ] **Step 1: Write the parity test**

Create `src/Api.Tests/SaleAssistParityTests.cs`:

```csharp
using System.Text.Json;
using Api.Decision;
using Api.SaleAssist;

namespace Api.Tests;

public class SaleAssistParityTests
{
    static readonly string[] DeterministicKeys =
    [
        "object_name", "details", "marketplace_search_url", "reshopper_relevant",
        "reshopper_url", "reshopper_note", "ad_text", "marketplace_note",
    ];

    public record Case(string Name, Assessment Assessment,
                       Dictionary<string, string?> Answers, JsonElement Expected);

    public static TheoryData<Case> Cases()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var json = File.ReadAllText("fixtures/sale-cases.json");
        var data = new TheoryData<Case>();
        foreach (var item in JsonSerializer.Deserialize<List<Case>>(json, options)!)
            data.Add(item);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Csharp_output_matches_python(Case testCase)
    {
        var builder = new SaleAssistBuilder(new EmptyPriceSearch());
        var recommendation = DecisionEngine.BuildRecommendation(testCase.Assessment, testCase.Answers);

        var actual = await builder.BuildAsync(
            testCase.Assessment, testCase.Answers, recommendation, CancellationToken.None);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var actualJson = JsonSerializer.SerializeToElement(actual, options);

        foreach (var key in DeterministicKeys)
        {
            Assert.Equal(
                testCase.Expected.GetProperty(key).ToString(),
                actualJson.GetProperty(key).ToString());
        }
    }
}
```

`EmptyPriceSearch` is a stub returning the same shape `PriceSearchClient` returns on failure, so the heuristic estimate path is what gets compared:

```csharp
sealed class EmptyPriceSearch : IPriceSearch
{
    public Task<PriceSignals> SearchAsync(string query, bool includeReshopper, CancellationToken ct) =>
        Task.FromResult(new PriceSignals(
            Url: $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
            Prices: [], Signals: [], Comparables: [], Confidence: "lav",
            Note: "Net-søgningen kunne ikke gennemføres fra prototypen. Linket åbner en manuel søgning efter lignende genstande."));
}
```

This requires a one-method `IPriceSearch` interface so the stub can substitute for `PriceSearchClient`. Add it in Task 10 alongside the client — it exists for testability, not for the provider swap discussed in the spec, and has exactly one production implementation.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter SaleAssistParityTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

Port `legacy/server.py:877-987` — `build_ad_text`, `build_ad_title`, `build_ad_feature_lines`, `build_ad_condition_lines`, `build_ad_sales_points` — then `build_sale_assist` (`:405-434`) as the composer. `marketplace_note` is a fixed three-sentence Danish string; copy it verbatim from `:426-430`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/Api.Tests`
Expected: PASS, all suites.

- [ ] **Step 5: Commit**

```bash
git add src/Api/SaleAssist src/Api.Tests/SaleAssistParityTests.cs
git commit -m "feat(sale): port ad text builder and sale assist composition"
```

---

### Task 13: Api host — endpoints, auth, rate limiting

**Files:**
- Modify: `src/Api/Program.cs`
- Create: `src/Api/Endpoints/AnalyzeEndpoints.cs`
- Create: `src/Api/Endpoints/RecommendEndpoints.cs`
- Create: `src/Api/Endpoints/SaleAssistEndpoints.cs`
- Modify: `src/Cirkulaer.ServiceDefaults/Extensions.cs`
- Test: `src/Api.Tests/EndpointContractTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 4-12
- Produces: three POST endpoints matching the Python routes and response envelopes: `/api/analyze` → `{"assessment": ...}`, `/api/recommend` → `{"recommendation": ...}`, `/api/sale-assist` → `{"sale": ...}`.

- [ ] **Step 1: Copy `UseApiKeyAuth` into ServiceDefaults**

Copy the `UseApiKeyAuth` extension from `C:\Development\Affaldssortering\src\Affaldssortering.ServiceDefaults\Extensions.cs:117-135` into `src/Cirkulaer.ServiceDefaults/Extensions.cs`, changing only the namespace. It returns early when `Auth:ApiKey` is unset, so local development needs no key.

- [ ] **Step 2: Write the failing contract test**

Create `src/Api.Tests/EndpointContractTests.cs` using `WebApplicationFactory<Program>`. Add `Microsoft.AspNetCore.Mvc.Testing` to `Api.Tests.csproj` and a `public partial class Program { }` line at the end of `src/Api/Program.cs` so the factory can reach it.

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

public class EndpointContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Recommend_returns_a_recommendation_envelope()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/recommend", new
        {
            assessment = new { object_name = "Kontorstol", category = "Møbler og indbo" },
            answers = new { works = "yes", reason = "no_need" },
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.True(body!.ContainsKey("recommendation"));
    }

    [Fact]
    public async Task Malformed_body_returns_400_with_error_key()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/recommend", new { assessment = "not an object" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.True(body!.ContainsKey("error"));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test src/Api.Tests --filter EndpointContractTests`
Expected: FAIL.

- [ ] **Step 4: Implement `Program.cs`**

Model it on `C:\Development\Affaldssortering\src\Api\Program.cs` — read it first. Required, in order:

```csharp
builder.AddServiceDefaults();
// ForwardedHeaders: XForwardedFor | XForwardedProto, KnownIPNetworks.Clear(), KnownProxies.Clear()
// AddRateLimiter: fixed window, RateLimit:RequestsPerMinute (default 20), per RemoteIpAddress,
//                 QueueLimit 0, RejectionStatusCode 429, OnRejected sets Retry-After: 60
// AddHttpClient for the vision providers and PriceSearchClient
// register VisionProviderFactory, SaleAssistBuilder
var app = builder.Build();
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseApiKeyAuth();
app.MapDefaultEndpoints();
app.MapAnalyzeEndpoints();
app.MapRecommendEndpoints();
app.MapSaleAssistEndpoints();
app.Run();

public partial class Program { }
```

Endpoint validation mirrors `legacy/server.py:89-166`: a non-object `assessment` or `answers` is a 400 with the Danish message `"Assessment og svar skal sendes som objekter."` (recommend) or `"Assessment skal sendes som objekt."` (sale-assist).

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test src/Api.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Api src/Cirkulaer.ServiceDefaults src/Api.Tests/EndpointContractTests.cs
git commit -m "feat(api): add endpoints, api key auth and per-ip rate limiting"
```

---

### Task 14: App host and frontend wiring

**Files:**
- Modify: `src/App/Program.cs`
- Modify: `src/App/wwwroot/app.js` (3 call sites + bootstrap)
- Create: `src/App/wwwroot/config.json`

**Interfaces:**
- Consumes: nothing from Api at build time; resolves `apiBaseUrl` at runtime
- Produces: a static host serving `wwwroot/` with `GET /config` returning `{ apiBaseUrl, apiKey }`

- [ ] **Step 1: Write `App/Program.cs`**

Copy `C:\Development\Affaldssortering\src\App\Program.cs` verbatim, changing only the service-discovery key from `services:api:...` to match this AppHost's `api` resource name (it is also `api`, so likely no change). Read that file first.

- [ ] **Step 2: Add the production config fallback**

Create `src/App/wwwroot/config.json`:

```json
{ "apiBaseUrl": "" }
```

azd rewrites this at deploy time; empty means same-origin.

- [ ] **Step 3: Add the config bootstrap to `app.js`**

At the top of `src/App/wwwroot/app.js`, before the first use:

```js
let apiConfig = { apiBaseUrl: "", apiKey: "" };

async function loadApiConfig() {
  try {
    apiConfig = await fetch("/config").then((response) => response.json());
  } catch {
    apiConfig = { apiBaseUrl: "", apiKey: "" };
  }
}

function apiUrl(path) {
  return new URL(path, apiConfig.apiBaseUrl || window.location.href);
}

function apiHeaders() {
  return { "Content-Type": "application/json", "X-Api-Key": apiConfig.apiKey };
}
```

Call `await loadApiConfig()` in the existing startup path — find where the app first initialises and add it there.

- [ ] **Step 4: Update the three fetch call sites**

At `app.js:233`, `:684`, `:996` (line numbers before this task's edits), replace `new URL("/api/x", window.location.href)` with `apiUrl("/api/x")` and the headers object with `apiHeaders()`. Keep the request bodies untouched.

- [ ] **Step 5: Verify manually**

Run: `dotnet run --project src/Cirkulaer.AppHost`

Open the App URL from the Aspire dashboard, upload a photo, walk the flow to a recommendation. Expected: identical behaviour to `python legacy/server.py`. Check the browser network tab shows `X-Api-Key` on the three API calls.

- [ ] **Step 6: Commit**

```bash
git add src/App
git commit -m "feat(app): serve frontend with /config bootstrap and api key header"
```

---

### Task 15: Aspire AppHost wiring

**Files:**
- Modify: `src/Cirkulaer.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: the `Api` and `App` projects
- Produces: a runnable `dotnet run --project src/Cirkulaer.AppHost` and the resource graph azd reads

- [ ] **Step 1: Write the AppHost**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var openAiApiKey = builder.AddParameter("openAiApiKey", secret: true);
var anthropicApiKey = builder.AddParameter("anthropicApiKey", secret: true);
var geminiApiKey = builder.AddParameter("geminiApiKey", secret: true);
var authApiKey = builder.AddParameter("authApiKey", secret: true);

var api = builder.AddProject<Projects.Api>("api")
    .WithExternalHttpEndpoints()
    .WithEnvironment("Ai__OpenAiApiKey", openAiApiKey)
    .WithEnvironment("Ai__AnthropicApiKey", anthropicApiKey)
    .WithEnvironment("Ai__GeminiApiKey", geminiApiKey)
    .WithEnvironment("Auth__ApiKey", authApiKey);

builder.AddProject<Projects.App>("app")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WithEnvironment("Auth__ApiKey", authApiKey)
    .WaitFor(api);

builder.Build().Run();
```

- [ ] **Step 2: Set local secrets**

```bash
cd src/Cirkulaer.AppHost
dotnet user-secrets set "Parameters:openAiApiKey" "<key or empty>"
dotnet user-secrets set "Parameters:anthropicApiKey" ""
dotnet user-secrets set "Parameters:geminiApiKey" ""
dotnet user-secrets set "Parameters:authApiKey" "local-dev-key"
```

- [ ] **Step 3: Run and verify**

Run: `dotnet run --project src/Cirkulaer.AppHost`
Expected: the Aspire dashboard lists `api` and `app`, both healthy, `app` starting after `api`.

- [ ] **Step 4: Commit**

```bash
git add src/Cirkulaer.AppHost
git commit -m "feat(apphost): wire api and app with provider keys"
```

---

### Task 16: azd configuration and GitHub Actions

**Files:**
- Create: `src/azure.yaml`
- Create: `src/infra/` (generated)
- Create: `.github/workflows/azure-dev.yml`

**Interfaces:**
- Consumes: the AppHost resource graph (Task 15)
- Produces: a deployable azd project

- [ ] **Step 1: Write `src/azure.yaml`**

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: src
services:
  app:
    language: dotnet
    project: ./Cirkulaer.AppHost/Cirkulaer.AppHost.csproj
    host: containerapp
```

- [ ] **Step 2: Generate the infrastructure**

```bash
cd src
azd init --environment cirkulaer-dev
azd infra synth
```

Expected: `src/infra/main.bicep` and `src/infra/resources.bicep` appear, containing a managed identity, container registry, Log Analytics workspace, Container Apps environment, and two container apps. Confirm there is **no** storage account, file share or `managedEnvironments/storages` resource — their presence means a stray stateful resource crept in.

- [ ] **Step 3: Write the workflow**

Copy `C:\Development\Affaldssortering\.github\workflows\azure-dev.yml` to `.github/workflows/azure-dev.yml` and change: trigger branch `master` → `main`; drop the postgres, dbadmin and rag deploy steps; keep `working-directory: src`; keep the deploy steps **sequential** (`azd deploy api` then `azd deploy app`) with the OOM comment intact; replace the secret list with `AZURE_OPEN_AI_API_KEY`, `AZURE_ANTHROPIC_API_KEY`, `AZURE_GEMINI_API_KEY`, `AZURE_AUTH_API_KEY`.

- [ ] **Step 4: Deploy**

```bash
cd src && azd up
```

Expected: provisioning succeeds, two container apps deployed, azd prints the App URL.

- [ ] **Step 5: Verify the deployment**

Open the App URL. Walk the full flow: photo → questions → recommendation → sale assist. Then check whether the price search survives Azure:

```bash
az containerapp logs show -n api -g rg-cirkulaer-dev --tail 200 | grep -i "price search"
```

Expected: either no warning (the scrape works from Azure) or the logged warning from Task 10. **Record which.** This is the data the follow-up decision in the spec depends on.

- [ ] **Step 6: Commit**

```bash
git add src/azure.yaml src/infra .github/workflows/azure-dev.yml
git commit -m "ci: add azd config, generated infra and deploy workflow"
```

---

### Task 17: Documentation and legacy removal

**Files:**
- Modify: `README.md`
- Verify: `Dokumentation/Beslutningstræ.md`
- Delete: `legacy/`
- Modify: `scripts/dump-legacy-fixtures.py` (delete alongside `legacy/`)

**Interfaces:**
- Consumes: a green test suite and a working deployment
- Produces: a repo `dinenergi` can pick up without knowing .NET

- [ ] **Step 1: Confirm parity is actually green**

Run: `dotnet test src/Cirkulaer.sln`
Expected: PASS, including `DecisionParityTests` and `SaleAssistParityTests`. Do not proceed if anything is skipped — a skipped parity test means the port is unverified.

- [ ] **Step 2: Check the decision tree doc still matches**

Read `Dokumentation/Beslutningstræ.md` against `src/Api/Decision/DecisionEngine.cs`. The documented order is `Reparér → Rens/klargør → Sælg → Bortgiv → Affald`. If the port changed anything the doc describes, the port is wrong — fix the code, not the doc.

- [ ] **Step 3: Rewrite `README.md`**

Keep the existing Danish sections describing the decision flow and producer programs. Replace the "Kør på computer" and "Kør fra mobiltelefon" sections with:

- `dotnet run --project src/Cirkulaer.AppHost`, and that the Aspire dashboard prints both URLs
- how to set provider keys via `dotnet user-secrets` (list the four parameter names from Task 15)
- that with no keys set, `/api/analyze` falls back to `LocalTestProvider` and marks results as a test, preserving the current documented behaviour
- how to add a producer program: append an object to `src/Api/data/producer-programs.json`, matching the existing fields; no code change needed
- that `X-Api-Key` is required in production and supplied by `App`'s `/config`

Update "Vigtige filer" to point at the C# paths.

- [ ] **Step 4: Delete the legacy Python**

```bash
git rm -r legacy scripts/dump-legacy-fixtures.py
```

The fixtures in `src/Api.Tests/fixtures/` stay — they are the committed record of the behaviour that was ported.

- [ ] **Step 5: Final build and test**

Run: `dotnet build src/Cirkulaer.sln && dotnet test src/Cirkulaer.sln`
Expected: both succeed.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "docs: rewrite readme for .NET, remove legacy python"
```

---

## Notes for the executor

- **Parity failures are port bugs.** When `DecisionParityTests` or `SaleAssistParityTests` fail, the C# is wrong. Never regenerate or edit a fixture to make a test pass.
- **The Danish strings are the product.** A test comparing `"Bruger den ikke længere"` is not a brittle string assertion — it is the user-visible output.
- **Task 10 is expected to be fragile.** The DuckDuckGo fixture may be a challenge page. That is a finding to report, not a blocker: the fallback path is fully tested and the app works without it.
- **Read the Python before porting each function.** The plan cites exact line numbers in `legacy/`; the behaviour lives there, not in this document's summaries.
