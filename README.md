# Cirkulær assistent

Mobilvenlig MVP-prototype for et cirkulært beslutningsflow:

```text
Tag billede -> svar på få spørgsmål -> få anbefaling -> handl
```

Prototypen bruger OpenAI-billedanalyse via et lokalt Python-endpoint og en
regelbaseret beslutningsmotor på backend. Affaldsvejledning er stadig generel,
indtil der kobles verificerede kommunale regler på.

## Kør på computer

Start serveren med en OpenAI API-nøgle:

```powershell
$env:OPENAI_API_KEY="din_noegle"
python server.py
```

Åbn derefter `http://127.0.0.1:4173`.

## Kør fra mobiltelefon

1. Sørg for at computer og telefon er på samme Wi-Fi.
2. Start serveren på computeren:

```powershell
$env:OPENAI_API_KEY="din_noegle"
python server.py
```

3. Kig efter linjen `Mobile: http://...:4173` i terminalen.
4. Åbn den adresse i browseren på telefonen.

Hvis telefonen ikke kan åbne siden, skal Windows Firewall tillade indgående
forbindelser til Python på port `4173`, eller serveren skal startes på en anden
ledig port:

```powershell
$env:PORT="4180"
python server.py
```

## Næste tekniske skridt

- Tilføj datamodel for kommunespecifikke affaldsregler med kildeangivelse.
- Gem billeder kortvarigt eller anonymiseret efter eksplicit privatlivsdesign.
- Udvid backend-testene med flere varetyper og edge cases.
## Beslutningstrae

Appen bruger nu det cirkulaere beslutningstrae som backend-logik:

```text
Foto -> AI-identifikation -> supplerende spoergsmaal -> hvorfor vil brugeren af med genstanden?
```

Derefter kontrolleres mulighederne i denne raekkefoelge:

```text
Reparer -> Saelg -> Bortgiv -> Affald
```

Hvis brugeren selv mener, at genstanden er affald, kontrollerer motoren stadig
foerst reparation, salg og bortgivelse. Affald anbefales kun, naar de andre
muligheder vurderes urealistiske.
## Producentordninger

IKEA Gensalg er nu modelleret som foerste konkrete producentordning, ikke som
hardcoded kernefunktion. Beslutningsmotoren kalder `producer_programs.py`, som
senere kan udvides med producenters reparation, reservedele, buy-back/gensalg,
trade-in, take-back og refurbishment.

IKEA-data bruges forsigtigt: appen markerer kun, om en vare potentielt ser
relevant ud, og linker videre til IKEAs officielle vurderingsvaerktoej. Endelig
pris og godkendelse afgoeres af IKEA.
## Testversion med billede

Appen kan nu bruges med et uploadet eller taget billede uden API-noegle. Hvis
`OPENAI_API_KEY` ikke er sat, returnerer serveren en tydeligt markeret lokal
testanalyse, saa hele flowet kan afproeves fra billede til anbefaling.

Hvis `OPENAI_API_KEY` er sat, bruger samme endpoint rigtig AI-billedanalyse.
