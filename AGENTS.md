# Cirkulær assistent — Project Notes

## What It Is

Danish circular-economy assistant. User photographs an object → AI identifies it → a few
questions → recommendation on the circular ladder:

```
Reparér → Rens/klargør → Sælg → Bortgiv → Affald
```

Waste is the last resort, never the first answer. The full decision tree is documented in
`Dokumentation/Beslutningstræ.md` — it must stay in sync with `Api/Decision/DecisionTree.cs`.

## Status

Mid-port from Python to .NET. Tasks 1-6 of 17 are done; the plan is
`docs/superpowers/plans/2026-09-09-cirkulaer-dotnet-restructure.md` and the design it
implements is `docs/superpowers/specs/2026-09-09-cirkulaer-dotnet-restructure-design.md`.
Read both before making structural changes.

`legacy/` holds the original Python. It is the reference implementation and stays until
parity is verified; it is deleted in Task 17.

## Architecture

- **Cirkulaer.AppHost** — Aspire orchestration. Wires provider API keys into `Api` as
  environment variables. You cannot tell how `Api` gets its keys by reading `Api/Program.cs`;
  look in `AppHost.cs`.
- **Api** — all client-facing endpoints, rate-limited 20 req/min per IP, `X-Api-Key` auth
- **App** — static file server for `wwwroot/` plus `GET /config`, which hands the browser
  `apiBaseUrl` and `apiKey`

Dependency chain: `api` → `app` (`WaitFor`). No database — producer programs and settings
are JSON files in the repo.

## Endpoints

| Endpoint | Reaches the internet | Notes |
|---|---|---|
| `POST /api/analyze` | yes — OpenAI / Anthropic / Gemini | vision → structured assessment |
| `POST /api/recommend` | no | pure decision tree |
| `POST /api/sale-assist` | yes — scrapes DuckDuckGo HTML | price comparables + ad text |

## Tech Stack

.NET 10, ASP.NET Core Minimal APIs, Aspire, xUnit, Azure Container Apps via `azd`.
Frontend is hand-written vanilla JS — no framework, no npm, no bundler, no build step.

## Key Files

| File | Purpose |
|------|---------|
| `src/Cirkulaer.AppHost/AppHost.cs` | Service wiring, provider key parameters |
| `src/Api/Decision/DecisionTree.cs` | The decision ladder — the core of the product |
| `src/Api/Decision/DecisionEngine.cs` | `BuildContext`: answers + assessment → context |
| `src/Api/Decision/TextHelpers.cs` | Danish normalisation; see the trap below |
| `src/Api/Producers/ProducerPrograms.cs` | Producer scheme matching (IKEA Gensalg today) |
| `src/Api/data/producer-programs.json` | Add a scheme here; no code change needed |
| `src/App/wwwroot/app.js` | Entire frontend, 1544 lines, unchanged from the prototype |
| `src/Api.Tests/fixtures/` | Golden-file parity fixtures — read the rule below |

## Commands

```bash
dotnet build src/Cirkulaer.slnx
dotnet test src/Cirkulaer.slnx
dotnet run --project src/Cirkulaer.AppHost     # Aspire dashboard prints both URLs
```

Provider keys are optional locally — with none set, `/api/analyze` falls back to a local
test analysis and marks the result as such:

```bash
cd src/Cirkulaer.AppHost
dotnet user-secrets set "Parameters:openAiApiKey" "..."
dotnet user-secrets set "Parameters:authApiKey" "local-dev-key"
```

## Rules That Are Not Guessable

These are the mistakes most likely to be made here. Each has already cost something.

**Never edit a fixture to make a test pass.** `src/Api.Tests/fixtures/*.json` were dumped
from the Python implementation. A parity failure means the C# is wrong, not the fixture.
Regenerate them only via `scripts/dump-legacy-fixtures.py`, never by hand.

**Danish strings are product copy.** Every user-visible string was written deliberately.
Do not translate, paraphrase, spell-correct, or "improve" them. A test asserting
`"Bruger den ikke længere"` is testing the product, not being brittle.

**Tri-state answers stay `string?`.** The engine distinguishes `"yes"` / `"no"` /
`"partly"` / `"unknown"` / absent, and absent is meaningful — it is not the same as
`"unknown"`. Converting these to enums is exactly where behaviour drifts silently.

**`Normalize` and `CanonicalCategoryId` are different operations.** `Normalize`
transliterates (`æ→ae`, `ø→oe`, `å→aa`). `CanonicalCategoryId` matches raw lowercased text
and lists both spellings itself. Do not merge them. Note `"Cykler"` classifies as `"other"`,
not `"bicycle"`, because the branch matches the substring `"cykel"` — a real bug in the
original, preserved deliberately and pinned by a test.

**The price scrape is expected to fail in production.** `PriceSearchClient` scrapes
DuckDuckGo HTML, which datacenter IPs get blocked from. Its failure path returns empty
prices and the estimator falls back to a category heuristic with an honest Danish note.
That degradation is the design — do not "fix" it. Replacing the search with a real API is
a documented follow-up, not a bug.

**`Content Update`, not `Content Include`.** The Web SDK already auto-includes JSON under
the project; `Include` fails the build with NETSDK1022.

## Deployment

GitHub Actions on push to `main` → `azd` → Azure Container Apps. `src/infra/` is generated
by `azd infra synth` from the AppHost — edit the AppHost, not the bicep. Deploy steps run
sequentially on purpose: concurrent `dotnet publish` OOM-kills the ubuntu-latest runner.
