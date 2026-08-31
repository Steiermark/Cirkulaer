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

Hvis `OPENAI_API_KEY` er sat:

- bruger `/api/analyze` rigtig OpenAI-billedanalyse
- billedanalysen returnerer strukturerede data
- beslutningsmotoren kører på backend

Hvis `OPENAI_API_KEY` ikke er sat:

- bruger `/api/analyze` lokal testanalyse
- billedet bliver stadig vist og brugt i flowet
- analysen markeres tydeligt som testversion

## Producentordninger

Producentordninger er samlet i:

```text
producer_programs.py
```

Modulet kan udvides med producenters:

- reparation
- reservedele
- buy-back/gensalg
- trade-in
- take-back
- refurbishment

IKEA Gensalg er første konkrete integration. Det er ikke hardcoded i kernen, men ligger som datapunkt i producentordningsmodulet.

## Kør på computer

Start serveren:

```powershell
python server.py
```

Med rigtig AI-billedanalyse:

```powershell
$env:OPENAI_API_KEY="din_nøgle"
python server.py
```

Åbn derefter:

```text
http://127.0.0.1:4173
```

## Kør fra mobiltelefon

1. Sørg for at computer og telefon er på samme Wi-Fi.
2. Start serveren på computeren.
3. Kig efter linjen `Mobile: http://...:4173` i terminalen.
4. Åbn den adresse i browseren på telefonen.

Hvis telefonen ikke kan åbne siden, skal Windows Firewall tillade indgående forbindelser til Python på port `4173`, eller serveren kan startes på en anden port:

```powershell
$env:PORT="4180"
python server.py
```

## Vigtige filer

- `index.html` - appens struktur
- `styles.css` - mobile-first design
- `app.js` - billedupload, spørgsmål og UI-flow
- `server.py` - lokal backend, AI/testanalyse og API-routes
- `decision_engine.py` - beslutningstræ og anbefaling
- `producer_programs.py` - producentordninger, inkl. IKEA Gensalg
- `test_decision_engine.py` - backend-tests

## Næste tekniske skridt

- Udvide producentordninger med flere brands og ordningstyper.
- Tilføje verificerede kommunale affaldsregler.
- Gemme billeder kortvarigt eller anonymiseret efter eksplicit privatlivsdesign.
- Flytte CO2- og økonomiestimater til datakilder frem for faste prototypeværdier.
- Udvide backend-tests med flere varetyper og edge cases.



