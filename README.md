# Cirkulær assistent

Mobilvenlig MVP-prototype for et cirkulært beslutningsflow:

```text
Tag billede -> svar på få spørgsmål -> få anbefaling -> handl
```

Prototypen bruger OpenAI-billedanalyse via et lokalt Python-endpoint og en
regelbaseret beslutningsmotor i browseren. Affaldsvejledning er stadig generel,
indtil der kobles verificerede kommunale regler på.

## Kør lokalt

Start serveren med en OpenAI API-nøgle:

```powershell
$env:OPENAI_API_KEY="din_noegle"
python server.py
```

Åbn derefter `http://127.0.0.1:4173`.

## Næste tekniske skridt

- Tilføj datamodel for kommunespecifikke affaldsregler med kildeangivelse.
- Gem billeder kortvarigt eller anonymiseret efter eksplicit privatlivsdesign.
- Udvid backend-testene med flere varetyper og edge cases.
