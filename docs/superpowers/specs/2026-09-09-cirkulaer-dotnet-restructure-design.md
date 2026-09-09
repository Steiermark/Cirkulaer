# Cirkulær assistent — .NET restructure and Azure deployment

**Date:** 2026-09-09
**Status:** Approved design, ready for implementation planning

## Problem

Cirkulaer is a flat prototype: `server.py` (1040 lines, Python stdlib only) serves both
the API and the static frontend from the repository root. It runs on a laptop and is
reached from a phone over Wi-Fi. There is no deployment story.

The goal is to host it in Azure the way `Affaldssortering` is hosted: .NET Aspire,
`azd`, Azure Container Apps, GitHub Actions on push, secrets in GitHub, HTTPS URL,
scale-to-zero.

`server.py` also serves the working directory through `SimpleHTTPRequestHandler` with no
`do_GET` override, so a public deployment of it as-is would expose `server.py`,
`decision_engine.py` and `.git/`. The restructure removes that class of problem by
separating static hosting from the API.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Backend language | Port to C# | Matches Affaldssortering; Aspire, azd, bicep and CI copy across nearly verbatim |
| Persistence | None | Producer programs and settings live in JSON files in the repo |
| LLM providers | OpenAI + Anthropic + Gemini | Copy Affaldssortering's provider abstraction; lets model quality be compared on photo assessment |
| Api exposure | External ingress, `X-Api-Key` + per-IP rate limit | Copy Affaldssortering exactly |
| Repo layout | `src/` for .NET, `legacy/` for Python until parity verified | Mirrors Affaldssortering; keeps a reference implementation during the port |
| Azure environment | Separate azd environment, own resource group and registry | Sharing fights azd's one-environment-per-project model |

### Ownership context

The Python was written by `dinenergi` with Codex. He is stack-agnostic and will adopt
the ported C# and continue from it, so the port does not fork an actively maintained
upstream — there is no dual-maintenance cost. Two consequences:

- The C# stays conventional and readable. The `IVisionProvider` seam and plain endpoint
  files are the ceiling on abstraction.
- Documentation is a deliverable. `python server.py` stops being how the project runs.

## Architecture

```
Browser (mobile-first, vanilla JS)
  GET  /config                       -> { apiBaseUrl, apiKey }
  POST {apiBaseUrl}/api/analyze      image(s) -> assessment      [AI]
  POST {apiBaseUrl}/api/recommend    assessment+answers -> rec   [pure]
  POST {apiBaseUrl}/api/sale-assist  -> price + ad text          [web search]
        |
        v
┌─────────────────────────────┐   ┌──────────────────────────────────┐
│ App        (ACA, external)  │   │ Api          (ACA, external)     │
│ wwwroot/ static             │   │ ForwardedHeaders                 │
│ GET /config                 │   │ RateLimiter  20/min per IP       │
└─────────────────────────────┘   │ UseApiKeyAuth  X-Api-Key         │
                                  │  Ai/       IVisionProvider       │
                                  │    -> OpenAI | Anthropic | Gemini│
                                  │  Decision/ pure decision tree    │
                                  │  Producers/ JSON-seeded          │
                                  │  SaleAssist/ -> duckduckgo.com   │
                                  └──────────────────────────────────┘
```

Aspire startup order: `api` → `app` (`WaitFor`). No Postgres, no Rag, no pgAdmin.

Two endpoints reach the internet. `/api/analyze` calls an LLM. `/api/sale-assist` scrapes
DuckDuckGo's HTML endpoint for comparable Danish prices. Only `/api/recommend` is a pure
function of its input.

## Target layout

```
Cirkulaer/
  src/
    Cirkulaer.AppHost/
    Cirkulaer.ServiceDefaults/
    Api/
      Ai/
        IVisionProvider.cs
        OpenAiVisionProvider.cs
        AnthropicVisionProvider.cs
        GeminiVisionProvider.cs
        LocalTestProvider.cs
        AssessmentSchema.cs
      Decision/
        DecisionEngine.cs
        Models.cs
      Producers/
        ProducerPrograms.cs
      SaleAssist/
        SaleAssistBuilder.cs
        SaleQueryBuilder.cs
        PriceSearchClient.cs
        PriceEstimator.cs
        AdTextBuilder.cs
      Endpoints/
        AnalyzeEndpoints.cs
        RecommendEndpoints.cs
        SaleAssistEndpoints.cs
      data/producer-programs.json
      Program.cs
    App/
      Program.cs
      wwwroot/  index.html  app.js  styles.css
    Api.Tests/
    Cirkulaer.sln
    azure.yaml
    infra/
  legacy/            <- Python, deleted once parity is verified
  Dokumentation/
  docs/superpowers/specs/
  README.md
  .github/workflows/azure-dev.yml
```

Target framework `net10.0`.

## Components

| File | Ported from | Notes |
|---|---|---|
| `Ai/IVisionProvider.cs` | — | `Task<Assessment> AnalyzeAsync(IReadOnlyList<string> imageDataUrls, CancellationToken ct)` |
| `Ai/OpenAiVisionProvider.cs` | `server.py:280-335` | Responses API, `input_image`, strict `json_schema` |
| `Ai/AnthropicVisionProvider.cs` | Affaldssortering `ClassifyEndpoints.cs` | Messages API with vision, adapted to Cirkulaer's schema |
| `Ai/GeminiVisionProvider.cs` | Affaldssortering `ClassifyEndpoints.cs` | `generateContent`, key via `x-goog-api-key` |
| `Ai/LocalTestProvider.cs` | `server.py` no-key branch | Preserves "runs without an API key" behaviour; result flagged as test |
| `Ai/AssessmentSchema.cs` | `ASSESSMENT_SCHEMA` | JSON schema + `Assessment` record |
| `Decision/DecisionEngine.cs` | `decision_engine.py` (407 lines) | Pure, no I/O |
| `Producers/ProducerPrograms.cs` + `data/producer-programs.json` | `producer_programs.py` (203 lines) | Data lifted out of code into JSON |
| `SaleAssist/SaleQueryBuilder.cs` | `server.py:436-527` | Search query, marketplace/Reshopper URLs. Pure |
| `SaleAssist/PriceSearchClient.cs` | `server.py:570-697` | Scrapes `duckduckgo.com/html/`, parses comparables and prices. **External HTTP** |
| `SaleAssist/PriceEstimator.cs` | `server.py:698-876` | Generic, bicycle and premium-bicycle estimates; age/damage factors; rounding. Pure |
| `SaleAssist/AdTextBuilder.cs` | `server.py:877-987` | Ad title, features, condition lines, sales points. Pure |
| `SaleAssist/SaleAssistBuilder.cs` | `server.py:405-435` | Composes the four above into the response |
| `Ai/TestAssessments.cs` | `server.py:184-284` | `build_test_assessment` fixtures used by `LocalTestProvider` and by tests |
| `Cirkulaer.ServiceDefaults` | Affaldssortering's ServiceDefaults | Holds `UseApiKeyAuth` and OTEL wiring; namespace renamed |

### Port hazards

**Danish normalisation.** `normalize()` maps `æ→ae`, `ø→oe`, `å→aa` and lowercases.
Producer `eligible_hints` / `excluded_hints` matching and category detection
(`"moebler"`, `"moebel"`, `"tekstil"`, `"elektronik"`) all run against normalised text.
Port it exactly and give it dedicated tests.

**Tri-state answers.** The decision engine distinguishes `"yes"` / `"no"` / `"partly"` /
`"unknown"` / absent, and Python's `dict.get()` returning `None` is meaningful — e.g.
`works in ("no", "partly", "unknown")` behaves differently from `works` being absent.
Keep these as `string?` with the exact Python literals rather than converting to C# enums
during the port; the mapping is where behaviour would silently drift. Enum cleanup is a
separate change afterwards, if wanted.

**Danish user-facing strings** are copied verbatim. They are product copy, not
placeholders.

## Error handling

Python returns `{"error": "..."}` with 400 on `ValueError` and 500 otherwise, and
`app.js` reads that exact shape. The C# must keep it byte-identical or the frontend's
error display breaks.

- Validation failure → 400 `{"error": "..."}` with the existing Danish message
- Provider call failure → 502 `{"error": "..."}`, Danish message
- No provider key configured → `LocalTestProvider`, response flagged as test analysis
- Rate limit exceeded → 429 with `Retry-After: 60`
- Missing or wrong `X-Api-Key` → 401
- Price search failure → **not** an error. `PriceSearchClient` catches everything and
  returns empty prices with confidence `"lav"` and a Danish note pointing at a manual
  search link, exactly as `server.py:580-590` does. `PriceEstimator` then falls back to
  its category/condition heuristic. This degrades quality, never the request.

## Frontend changes

`index.html` and `styles.css` move to `wwwroot/` unchanged. `app.js` needs a config
bootstrap and three modified call sites (`app.js:233`, `:684`, `:996`):

```js
const cfg = await fetch("/config").then(r => r.json());

fetch(new URL("/api/analyze", cfg.apiBaseUrl || window.location.href), {
  method: "POST",
  headers: { "Content-Type": "application/json", "X-Api-Key": cfg.apiKey },
  body: JSON.stringify(payload),
});
```

`App/Program.cs` mirrors Affaldssortering's: `UseDefaultFiles()`, `UseStaticFiles()`,
and a `/config` endpoint returning `apiBaseUrl` (from Aspire service discovery in
development, from `wwwroot/config.json` in production) plus `apiKey`.

## Testing

- `Api.Tests` (xUnit). `test_decision_engine.py`'s 425 lines become `[Theory]` cases with
  member data.
- **Golden-file parity.** A script runs the `legacy/` Python over a fixture set of
  `(assessment, answers)` pairs and dumps the JSON results. Tests assert the C# produces
  identical output. This is the reason `legacy/` survives until the end — the decision
  tree branches enough that the ported unit tests alone are thin cover.
- Dedicated tests for `normalize()` and for producer-program hint matching.
- Providers: no network tests. Parse captured response JSON against the schema.
- `PriceSearchClient` is tested against a saved DuckDuckGo HTML page fixture, never the
  live site. Everything downstream of it takes a price list as a parameter, so the
  estimator and ad-text tests need no network and no stubbing.

## Deployment

`src/azure.yaml` declares two services, `api` and `app`, host `containerapp`. `infra/`
is generated by `azd infra synth` from the AppHost rather than hand-written.

AppHost wiring:

```csharp
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
```

Workflow copied from Affaldssortering's `azure-dev.yml` with these changes:

- Trigger on `main`, not `master`
- `working-directory: src`
- Sequential `azd deploy api` then `azd deploy app` — concurrent `dotnet publish` steps
  OOM-kill the ubuntu-latest runner, a lesson already paid for in Affaldssortering
- Secrets: `AZURE_OPEN_AI_API_KEY`, `AZURE_ANTHROPIC_API_KEY`, `AZURE_GEMINI_API_KEY`,
  `AZURE_AUTH_API_KEY`
- No Postgres step, and no commented-out deploy step

Because there is no database, the generated infra has no storage account, no file share
and no `managedEnvironments/storages` resource, and no deploy can put data at risk.

## Documentation

`README.md` is rewritten: how to run via the AppHost, how to configure provider keys, how
to add a producer program to `producer-programs.json`. `Dokumentation/Beslutningstræ.md`
stays as the decision-tree reference and must still match `DecisionEngine.cs` after the
port.

## Port surface

| Source | Lines | Fate |
|---|---|---|
| `server.py:1-183` | 183 | Discarded — ASP.NET replaces the HTTP handler |
| `server.py:184-404` | 221 | Ported — test assessments, OpenAI call, `normalize_assessment`, `canonical_category_id` |
| `server.py:405-987` | 583 | Ported — sale assist, including the price scrape |
| `decision_engine.py` | 407 | Ported |
| `producer_programs.py` | 203 | Ported, data moved to JSON |
| `test_decision_engine.py` | 425 | Ported to xUnit |

Roughly 1400 lines of logic, not counting tests.

## Price search: known limitation and agreed follow-up

`PriceSearchClient` scrapes `https://duckduckgo.com/html/?q=...`. This works from a home
IP and is expected to fail from Azure's datacenter ranges, which get challenged or
blocked. It is ported as-is anyway, deliberately:

- Failure degrades quality, not availability. Every estimate path has a complete
  heuristic fallback and an honest Danish note saying no web prices were found.
- `dinenergi` already tests that path (`test_too_small_web_prices_fall_back_without_crashing`).
- Buying a search API before observing deployed behaviour would be guessing.

`PriceSearchClient` logs a warning when the search fails, so the deployed logs answer the
question. If they show it is blocked, the two candidate replacements are:

- **OpenAI Responses `web_search`** — preferred. The Api already holds the key and speaks
  the Responses API; structured JSON results would let the ~150 lines of HTML regex
  (`extract_comparables`, `extract_prices`, `clean_html_text`, `decode_search_result_url`)
  be deleted rather than maintained. ~$10 per 1,000 calls plus ~8k input tokens per search.
- **Brave Search API** — ~$5 per 1,000 queries. No free tier since February 2026; card
  required, no spending cap, attribution required.

The swap is cheap because `estimate_sale_price(assessment, answers, prices)` takes prices
as a parameter — the search is already a clean seam, and no test crosses it. Introduce an
`IPriceSearchProvider` interface at that point, not before; one implementation does not
need one.

## Deliberate deviations from the Python

- **Danish thousands separator.** `extract_prices`' regex required two leading digits, so
  `"1.250 kr."` parsed as `250`. Since Danish listings commonly use `.` as the thousands
  separator, this systematically dragged web-derived price estimates down. Fixed in the
  port at Jan's direction (2026-09-09). No parity fixture covers it — the sale fixture
  strips every search-derived key — so the change is invisible to the parity suite and is
  pinned by its own test instead.

## Out of scope

- Converting tri-state strings to enums
- Any change to the decision tree's behaviour
- Replacing the price search (documented above as a follow-up, decided from deployed logs)
- Municipal waste rules, CO2 data sources, image storage — roadmap items in the current
  README, untouched here
- Adding a database
