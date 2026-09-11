# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read first

`AGENTS.md` holds the full rules for this repo: the non-guessable traps, the settled
architectural decisions, and the deliberate deviations from the original Python. Read it
before changing anything structural. This file is the short operational entry point.

`AGENTS.md` is also read by Codex, which does the UI work here, so it stays the complete
self-contained source of truth. Do not move content out of it into this file, and record
new rules there rather than only here.

Design and plan docs live in `docs/superpowers/specs/` and `docs/superpowers/plans/`.
The decision tree is specified in `Dokumentation/Beslutningstræ.md` and must stay in sync
with `src/Api/Decision/DecisionTree.cs`.

## Commands

```powershell
dotnet build src/Cirkulaer.slnx
dotnet test src/Cirkulaer.slnx
dotnet test src/Cirkulaer.slnx --filter "FullyQualifiedName~DecisionParityTests"
dotnet test src/Cirkulaer.slnx --filter "FullyQualifiedName~TextHelpersTests.Normalize_Transliterates"

dotnet run --project src/Cirkulaer.AppHost   # whole stack; Aspire dashboard prints both URLs
node tools/static-wwwroot-server.mjs         # frontend only on :4173, no .NET, /api/* fails
```

Provider keys are optional locally — with none set, `/api/analyze` returns a local test
analysis flagged as such. To set them:

```powershell
cd src/Cirkulaer.AppHost
dotnet user-secrets set "Parameters:geminiApiKey" "..."
dotnet user-secrets set "Parameters:authApiKey" "local-dev-key"
```

`authApiKey` is the shared `X-Api-Key` between App and Api; App's `/config` hands it to the
browser.

## Architecture

Two ASP.NET Core apps plus Aspire orchestration, no database — producer programs and
settings are JSON files in the repo.

- `src/Cirkulaer.AppHost` — Aspire wiring. Provider API keys and `Ai__*` overrides reach
  `Api` as environment variables from here; `Api/Program.cs` does not reveal where its keys
  come from.
- `src/Api` — the three POST endpoints, rate-limited 20 req/min per IP, `X-Api-Key` auth.
  `Decision/` is the product core; `Ai/` holds the three vision providers plus the test
  fallback; `SaleAssist/` does price search, estimation and ad text.
- `src/App` — static server for `wwwroot/` plus `GET /config`, which returns `apiBaseUrl`
  and `apiKey` to the browser. Frontend is hand-written vanilla JS in `wwwroot/app.js` —
  no framework, no npm, no build step.
- `src/Cirkulaer.ServiceDefaults` — shared health checks, telemetry, and the raised HTTP
  resilience timeouts that vision calls depend on.

Dependency chain: `api` → `app` (`WaitFor`). App and Api are separate origins, so every
browser call is cross-origin and CORS is load-bearing.

| Endpoint | Reaches the internet |
|---|---|
| `POST /api/analyze` | yes — OpenAI / Anthropic / Gemini |
| `POST /api/recommend` | no, pure decision tree |
| `POST /api/sale-assist` | yes — dba.dk search page |

## Hard rules

- **Never edit a fixture to make a test pass.** `src/Api.Tests/fixtures/*.json` were dumped
  from the deleted Python implementation and cannot be regenerated. 2,216 golden cases; a
  parity failure means the C# is wrong. An intended behaviour change edits fixture and code
  in the same commit, with the reason in the message.
- **Danish strings are product copy.** Do not translate, paraphrase or spell-correct them.
- **Tri-state answers stay `string?`.** `"yes"` / `"no"` / `"partly"` / `"unknown"` / absent
  are five distinct states; absent is not `"unknown"`. Do not convert to enums.
- **Price search parses dba.dk's own search page.** One GET, ~1s, no model. OpenAI
  `web_search` and a DuckDuckGo scrape were both tried and measured dead — see AGENTS.md
  before reaching for an LLM here again. Falling back to the category heuristic when dba
  yields nothing is the design, not a bug.
- **Infrastructure is generated.** `src/infra/` comes from `azd infra synth` — edit
  `src/Cirkulaer.AppHost/AppHost.cs`, not the bicep.
- **JSON data files use `Content Update`, not `Content Include`** — the Web SDK already
  auto-includes them and `Include` fails the build with NETSDK1022.

Further traps (the `Normalize` / `CanonicalCategoryId` split, CORS preflight ordering, the
Aspire resilience-handler timeout, `azd` deploy sequencing) are documented in `AGENTS.md`.

## AI provider and model

Configuration, not code. Defaults in `src/Api/appsettings.json` under `Ai`; override per
environment with `Ai__DefaultProvider` and `Ai__Providers__<provider>__Model`, which the
AppHost forwards to the Api. A provider with no API key is skipped, so an unavailable
default falls back to one that has a key. `/api/analyze` also accepts a per-request
`provider` field, which `app.js` does not send today.

## Deployment

Push to `main` → GitHub Actions → `azd` → Azure Container Apps. Deploy services one at a
time (`azd provision`, then `azd deploy api`, then `azd deploy app`); concurrent publishes
collide on the registry push path and OOM the runner. `azd` exits 0 even when a deploy
fails, so read the output rather than trusting the exit code.
