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

Ported from Python to .NET and deployed to Azure Container Apps (2026-09-10). The design
and plan are in `docs/superpowers/`; read them before making structural changes.

The original Python is gone from the tree but remains in git history. Its behaviour is
pinned by 2,216 golden-file cases in `src/Api.Tests/fixtures/`, which were dumped from it
and are the contract the C# is verified against.

## Architecture

- **Cirkulaer.AppHost** — Aspire orchestration. Wires provider API keys into `Api` as
  environment variables. You cannot tell how `Api` gets its keys by reading `Api/Program.cs`;
  look in `AppHost.cs`.
- **Api** — all client-facing endpoints, rate-limited 20 req/min per IP, `X-Api-Key` auth
- **App** — static file server for `wwwroot/` plus `GET /config`, which hands the browser
  `apiBaseUrl` and `apiKey`

Dependency chain: `api` → `app` (`WaitFor`). No database — producer programs and settings
are JSON files in the repo.

```
Browser (mobile-first, vanilla JS, no framework/npm/build step)
  GET  /config                       -> { apiBaseUrl, apiKey }   [App]
  POST {apiBaseUrl}/api/analyze      image(s) -> assessment       [AI, ~10-30s]
  POST {apiBaseUrl}/api/recommend    assessment+answers -> rec    [pure, instant]
  POST {apiBaseUrl}/api/sale-assist  -> price + comparables + ad  [dba.dk search page ~1s, +~10s lookalike grading with photo]
```

`/api/recommend` is the only one that is a pure function of its input. The other two reach
the internet, cost money, and are slow enough that the UI must show progress.

### Where this is going

Direction, not yet built. Useful for judging whether a change fits.

- **A database is expected**, and the App/Api split is kept partly to make room for it.
  The roadmap that needs it: verified municipal waste rules, short-lived or anonymised
  image storage behind an explicit privacy design, and CO2 and price estimates sourced
  from data rather than the fixed prototype values in `PriceEstimator`.
- **Durable knowledge belongs in data, not code.** Producer schemes already work this way
  — a new scheme is a JSON object in `producer-programs.json`, no code change. The same
  shape is intended for waste rules and price bands.
- **Affaldssortering is the reference implementation.** Same maintainer, same stack, same
  idioms deliberately. Its `item_lookup` (curated table + accumulated misses + pgvector
  matching) is the model a price-history store would follow. Discussed 2026-09-11, **not
  decided** — a price band ages gracefully, but listing links do not, so only the numbers
  are worth persisting.
- **The abstraction ceiling stays low.** `IVisionProvider` and `IPriceSearch` are seams
  because vendors change. Plain endpoint files and a pure decision tree are the rest.
  Do not add layers the task does not require.

What this means for UI work: `/config` and the three endpoint contracts are the seam. The
shapes the frontend consumes — `assessment`, `recommendation`, `SaleAssistResult` with its
`comparables` and `price_confidence` — are stable and worth building against. How the
backend fills them is not, and is expected to change.

## Endpoints

| Endpoint | Reaches the internet | Notes |
|---|---|---|
| `POST /api/analyze` | yes — OpenAI / Anthropic / Gemini | vision → structured assessment |
| `POST /api/recommend` | no | pure decision tree |
| `POST /api/sale-assist` | yes — dba.dk search page, Gemini when a photo is sent | price comparables + ad text; ~1s, +~10s with photo |

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
| `src/Api/Ai/` | Vision providers (OpenAI, Anthropic, Gemini) + local test fallback |
| `src/Api/SaleAssist/` | Sale query, price search, price estimation, ad text |
| `src/Api/Endpoints/ApiEndpoints.cs` | The three POST endpoints and their validation |
| `src/App/wwwroot/app.js` | Entire frontend, ~1580 lines, essentially the prototype |
| `src/Api.Tests/fixtures/` | Golden-file parity fixtures — read the rule below |

## Commands

```bash
dotnet build src/Cirkulaer.slnx
dotnet test src/Cirkulaer.slnx
dotnet run --project src/Cirkulaer.AppHost     # Aspire dashboard prints both URLs

node tools/static-wwwroot-server.mjs           # frontend only, no .NET needed
```

`static-wwwroot-server.mjs` serves `src/App/wwwroot/` on port 4173 and binds `0.0.0.0`, so
a phone on the same Wi-Fi can open it. Use it for layout and flow work; `/api/*` calls fail
because no `Api` is running. `/config` is stubbed so `app.js` falls back to same-origin.

Provider keys are optional locally — with none set, `/api/analyze` falls back to a local
test analysis and marks the result as such:

```bash
cd src/Cirkulaer.AppHost
dotnet user-secrets set "Parameters:geminiApiKey" "..."
dotnet user-secrets set "Parameters:authApiKey" "local-dev-key"
```

## The Architecture Follows Affaldssortering

Anyone can work anywhere in this repo. The constraint is not who touches which file — it
is that **the architecture stays the same shape as Affaldssortering**, deliberately, for
one maintainer's sake. Adopting a pattern from there needs no discussion. Diverging from
it does, however local the change looks.

What "the same shape" means in practice:

- .NET with Aspire orchestration, deployed to Azure Container Apps by `azd`, with
  `src/infra/` generated from the AppHost rather than hand-written.
- App and Api as separate container apps, with `/config`, `X-Api-Key` and CORS as the seam
  between them.
- Vendor-facing seams only: `IVisionProvider`, `IPriceSearch`. Plain endpoint files and a
  pure decision tree everywhere else.
- Durable knowledge as JSON-seeded data, the way `producer-programs.json` works. When a
  database arrives it is Postgres, following `item_lookup`'s curated-table-plus-misses
  pattern.

So the question to ask before a backend change is not "is this mine?" but "does
Affaldssortering do it this way?" If it does, go ahead. If it doesn't, or if it has no
equivalent, raise it first.

One file deserves singling out because its name misleads: `src/App/Program.cs` looks like
frontend and is not. It serves `/config`, which decides the Api URL and scheme for every
browser call. A one-line change there took production down on 2026-09-11. Read the CORS
and scheme rules below before touching it.

## Settled Decisions

Do not re-open these without a new reason; they were considered and decided.

- **App and Api stay separate container apps**, mirroring Affaldssortering, even though
  Cirkulaer has no database or internal service today. The split is what makes `/config`,
  CORS and the browser-visible `X-Api-Key` necessary, and collapsing them would remove all
  three — but the roadmap (municipal waste rules, stored images, CO2 data sources) points at
  a database and more services, and keeping both projects the same shape matters for a
  single maintainer. Decided 2026-09-10.
- **Three vision providers are kept** even though only Gemini is used by default. Vendors
  leapfrog each other quickly and switching is now a config change, not a code change.

## Switching AI provider or model

Both are configuration, not code. Defaults live in `src/Api/appsettings.json`:

```json
"Ai": {
  "DefaultProvider": "gemini",
  "Providers": {
    "openai":    { "Model": "gpt-5" },
    "anthropic": { "Model": "claude-sonnet-5" },
    "gemini":    { "Model": "gemini-2.5-flash" }
  }
}
```

Override per environment with `Ai__DefaultProvider` and
`Ai__Providers__<provider>__Model`, which the AppHost forwards to the Api. In Azure, editing
those on the container app restarts the revision in about 30 seconds — no rebuild, no
redeploy. A provider whose API key is unset is skipped, so an unavailable default falls back
to one that has a key rather than failing.

A request may also name a provider per call via the `provider` field on `/api/analyze`,
which is how the same photo can be compared across vendors. `app.js` does not send it today.

## Rules That Are Not Guessable

These are the mistakes most likely to be made here. Each has already cost something.

**Never edit a fixture to make a test pass.** `src/Api.Tests/fixtures/*.json` were dumped
from the original Python implementation, which no longer exists in the tree. They cannot be
regenerated — they are a frozen record of the behaviour this code must reproduce. A parity
failure means the C# is wrong. If a behaviour change is genuinely intended, change the
fixture in the same commit as the code and say why in the message, the way the thousands-
separator fix did.

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

**Price search reads dba.dk's own search page, and that is the third attempt.**
`DbaPriceSearch` GETs `dba.dk/recommerce/forsale/search?q=<query>` and parses the
schema.org `ItemList` that dba server-renders in a `<script type="application/ld+json"
id="seoStructuredData">` block: title, price in DKK, and the `/recommerce/forsale/item/<id>`
url of every ad on the page. ~1s, no key, no model, and the links are dba's own. Any user
agent is served; dba's `robots.txt` allows the search path. Measured 2026-09-11. If dba
changes the markup the parser returns zero rows and the heuristic estimate takes over —
that is the intended failure, not a crash.

The two earlier designs are recorded so they are not retried:

- **OpenAI `web_search` (Responses API, `allowed_domains` dba.dk + guloggratis.dk),
  2026-09-11.** Worked once, for an IKEA BILLY, and that was luck. Its index holds dba
  *search* pages only — every source it cited was `/recommerce/forsale/search?q=…` — and no
  item pages, and `open_page` serves from OpenAI's crawl cache rather than fetching live: told
  the exact search url for a Roland FP-30X it answered "cache miss" while the page carried
  three ads. Niche items therefore returned `{"comparables": []}` after ~20s and ~$0.13, and
  the model was never given a way to do better. Prompt tightening made it worse: demanding
  direct links made it fabricate plausible `dba.dk` ids that all 404 while looking like an
  improvement (8 rows, `høj`, 44s). Gemini grounding was also rejected: it cannot open
  individual ads, and `url_context` suppresses the answer with `finishReason: RECITATION`.
- **DuckDuckGo HTML scrape, deleted 2026-09-10.** Result snippets carry no prices on any
  IP; the plain query gave one price string in 32 KB, and every `site:` variant answers
  HTTP 202 with zero results. It was never the Azure IP block AGENTS.md once blamed.

**A known model is searched bare; an unknown one by the vision step's search term.**
`BuildPriceQuery` sends dba "Roland FP-30X", not the sale query "Roland FP-30X Digitalpiano
med stativ brugt pris Danmark": dba narrows on every descriptive word and loosens on the
suffix (2 rows vs 50+ of any Roland vs 6 exact, measured 2026-09-11). Without a model it
sends the first of `assessment.search_terms`, which the vision prompt asks for as "what a
Danish seller would title the ad" — "PH 5 pendel sort hvid" finds look-alikes where the
object name "Pendellampe" found seven strangers. The sale query still feeds the
marketplace and manual links, which the parity fixture pins. `search_terms` is the one
addition to the legacy assessment schema.

**A photo narrows the comparables to look-alikes, in a second request.** `app.js` first
calls sale-assist without images and renders that in ~1s; if there is a photo and three or
more rows it calls again with the first photo and swaps the result in when it lands. On
the Api side, when dba returns any rows and a photo came along, `LookalikeFilter` fetches up
to 12 ad thumbnails from dba's structured data and asks Gemini Flash to grade each as
`same` / `similar` / `different` against the photo *and the ad titles* — an FP-30 and an
FP-30X are visually identical, so model designations in the title outweigh looks. The
cascade is `same` → `similar` → every row, and the search note names the rung, so a
price built on look-alikes or on the plain result says so. Rows past the thumbnail cap
are dropped on the first two rungs, not kept unjudged. A failed or malformed grading
keeps every row, logged. Measured 2026-09-11: a PH 5 photo
under the generic query "Pendel Lampe" went from 52 rows at 300 kr to one PH-style
pendant at 850 kr; under "Louis Poulsen PH 5" all graded `same`. Adds ~10s locally, ~25s from Azure with 20
thumbnails, hence the cap of 12. The
photo passes through and is not stored.

**GulogGratis is not searched.** It sits behind Cloudflare and answers datacenter IPs with
a challenge, even for `robots.txt`. Do not add it by spoofing a browser user agent.

**App and Api are separate origins, so CORS is load-bearing.** Every browser call is
cross-origin and `X-Api-Key` forces a preflight. `UseCors()` must run before
`UseApiKeyAuth()`, and the key gate must let `OPTIONS` through — preflights carry no custom
headers, so gating them yields a 401 that the browser reports only as "Failed to fetch" /
"Load failed". `curl` never sends a preflight, so this is invisible to command-line testing;
verify in a real browser or rely on the preflight test in `EndpointContractTests`.

**Aspire's default resilience handler cancels vision calls.** `ServiceDefaults` applies
`AddStandardResilienceHandler()` to every HttpClient, whose per-attempt timeout is 10s and
total 30s — far shorter than a vision call on a 1.5 MB photo, and it overrides
`client.Timeout`. Symptom: HTTP 500 with an **empty body** after ~30s, and a Polly stack
trace in the container logs. Cirkulaer raises these to 90s/180s in
`Cirkulaer.ServiceDefaults/Extensions.cs`; do not revert to the template defaults.

**Deploy services one at a time; `azd up` publishes them concurrently and they collide**
on the registry push path (`CONTAINER1013: Access to the path is denied`, plus a misleading
`empty dotnet configuration output` for the other service). Use `azd provision` then
`azd deploy api` then `azd deploy app`. Note `azd` exits 0 even when a deploy fails, so
check the output text and confirm the container apps exist.

**Set secure parameters via `azd env config set infra.parameters.<name>`, not `azd env set`.**
Provisioning resolves them from environment variables, but `azd deploy` resolves them from
`infra.parameters` and fails with `parameter <name> not found` if only the env var is set.
An empty value also has to go through the config form — `azd env set NAME ""` leaves the
parameter unset and provisioning stops with "1 required input is missing" while exiting 0.
In CI the workflow passes env vars, so every GitHub secret must exist and be non-empty
where the service actually needs it.

**`Content Update`, not `Content Include`.** The Web SDK already auto-includes JSON under
the project; `Include` fails the build with NETSDK1022.

## Deliberate Deviations From The Python

Behaviour that intentionally differs from `legacy/`. Do not "restore" these.

- **Danish thousands separator in prices.** The original regex required two leading digits,
  so `"1.250 kr."` parsed as `250` and biased every web-derived estimate downward. Fixed in
  `PriceSearchClient.ExtractPrices` (`\d{2,6}` → `\d{1,6}`). That client was deleted on
  2026-09-10 along with the scrape, so the regex is gone — but the fix is still why the sale
  fixtures differ from the Python. Don't read a price-related fixture diff as a regression
  without checking this first.

## Deployment

GitHub Actions on push to `main` → `azd` → Azure Container Apps. `src/infra/` is generated
by `azd infra synth` from the AppHost — edit the AppHost, not the bicep. Deploy steps run
sequentially on purpose: concurrent `dotnet publish` OOM-kills the ubuntu-latest runner.
