# Cirkulær assistent

Mobilvenlig MVP/testprototype for et cirkulært beslutningsflow:

```text
Tag billede -> identificér genstand -> svar på få spørgsmål -> få anbefaling -> handl
```

Appen vurderer først, om genstanden stadig kan skabe værdi. Affald er sidste mulighed.

## Beslutningsflow

Den aktuelle prioritering er:

```text
Reparér -> Rens/klargør -> Sælg -> Bortgiv -> Affald
```

`Rens/klargør` bruges især for møbler, hvor værdien ofte kan løftes uden teknisk reparation.

Det fulde beslutningstræ ligger her:

[Dokumentation/Beslutningstræ.md](Dokumentation/Beslutningstræ.md)

## Testversion med billede

Appen kan bruges med et uploadet eller taget billede.

Hvis en AI-nøgle er sat (OpenAI, Anthropic eller Gemini):

- bruger `/api/analyze` rigtig billedanalyse
- billedanalysen returnerer strukturerede data
- beslutningsmotoren kører på backend

Hvis ingen AI-nøgle er sat:

- bruger `/api/analyze` lokal testanalyse
- billedet bliver stadig vist og brugt i flowet
- analysen markeres tydeligt som testversion

## Producentordninger

Producentordninger er samlet i:

```text
src/Api/data/producer-programs.json
```

En ny ordning tilføjes ved at indsætte et objekt i JSON-filen med de samme felter som
IKEA Gensalg. Det kræver ingen kodeændring.

Modulet kan udvides med producenters:

- reparation
- reservedele
- buy-back/gensalg
- trade-in
- take-back
- refurbishment

IKEA Gensalg er første konkrete integration. Det er ikke hardcoded i kernen, men ligger som datapunkt i producentordningsmodulet.

## Kør på computer

Hele stakken (Api + App) startes via Aspire:

```powershell
dotnet run --project src/Cirkulaer.AppHost
```

Aspire-dashboardet viser adresserne på begge services. Appen er `app`-ressourcen.

AI-nøgler er valgfrie lokalt. Uden nøgler falder `/api/analyze` tilbage til lokal
testanalyse, og svaret markeres tydeligt som testversion. Med nøgler:

```powershell
cd src/Cirkulaer.AppHost
dotnet user-secrets set "Parameters:openAiApiKey" "..."
dotnet user-secrets set "Parameters:anthropicApiKey" "..."
dotnet user-secrets set "Parameters:geminiApiKey" "..."
dotnet user-secrets set "Parameters:authApiKey" "local-dev-key"
```

`authApiKey` er den delte `X-Api-Key` mellem App og Api. Den udleveres til browseren af
App'ens `/config`, så frontend kan kalde Api.

## Kør kun frontend

Til arbejde med HTML, CSS og JavaScript er der en frontend-server uden .NET:

```powershell
node tools/static-wwwroot-server.mjs
```

Den serverer `src/App/wwwroot/` på port 4173 og lytter på alle netkort, så en telefon på
samme Wi-Fi kan åbne den. `/api/*` fejler, fordi Api ikke kører — brug den til layout og
flow, ikke til at teste hele kæden.

## Kør fra mobiltelefon

1. Sørg for at computer og telefon er på samme Wi-Fi.
2. Start `node tools/static-wwwroot-server.mjs`.
3. Kig efter linjen `Mobile: http://...:4173` i terminalen.
4. Åbn den adresse i browseren på telefonen.

Hvis telefonen ikke kan åbne siden, skal Windows Firewall tillade indgående forbindelser
på porten, eller serveren kan startes på en anden port:

```powershell
$env:PORT="4180"
node tools/static-wwwroot-server.mjs
```

## Test

```powershell
dotnet test src/Cirkulaer.slnx
```

Testene indeholder 2.216 golden-file-tests, der sammenligner output med den oprindelige
Python-implementering. Slår en af dem fejl, er C#-koden forkert — rettelser hører hjemme
i koden, ikke i testdata under `src/Api.Tests/fixtures/`.

## Deploy

Push til `main` bygger og deployer automatisk til Azure Container Apps via GitHub Actions
og `azd`. Infrastrukturen genereres fra Aspire-AppHost'en, så ændringer hører hjemme i
`src/Cirkulaer.AppHost/AppHost.cs` — ikke i `src/infra/`.

## Vigtige filer

- `src/App/wwwroot/index.html` - appens struktur
- `src/App/wwwroot/styles.css` - mobile-first design
- `src/App/wwwroot/app.js` - billedupload, spørgsmål og UI-flow
- `src/App/Program.cs` - statisk webserver og `/config`
- `src/Api/Decision/DecisionTree.cs` - beslutningstræ og anbefaling
- `src/Api/Ai/` - billedanalyse via OpenAI, Anthropic og Gemini
- `src/Api/SaleAssist/` - prisvurdering, annoncetekst og salgslinks
- `src/Api/data/producer-programs.json` - producentordninger, inkl. IKEA Gensalg
- `src/Api.Tests/` - backend-tests
- `AGENTS.md` - arkitektur og regler for udviklere og AI-assistenter

## Næste tekniske skridt

- Udvide producentordninger med flere brands og ordningstyper.
- Tilføje verificerede kommunale affaldsregler.
- Gemme billeder kortvarigt eller anonymiseret efter eksplicit privatlivsdesign.
- Flytte CO2- og økonomiestimater til datakilder frem for faste prototypeværdier.
- Udvide backend-tests med flere varetyper og edge cases.

## Ændringer i forhold til Python-prototypen

Porten til .NET er en 1:1-oversættelse af forretningslogikken, verificeret med
golden-file-tests. Én bevidst rettelse:

- **Tusindtalsseparator i priser.** Den oprindelige regex krævede to indledende cifre, så
  `"1.250 kr."` blev læst som `250`. Da danske annoncer typisk bruger `.` som
  tusindtalsseparator, trak det systematisk prisestimaterne ned. Rettet i porten.

Web-søgningen efter sammenlignelige priser kan blive blokeret fra Azures IP-adresser. Sker
det, falder prisestimatet tilbage til kategori- og standbaserede intervaller, og noten
oplyser, at der ikke blev fundet webpriser. Det er tilsigtet — appen fejler ikke.
