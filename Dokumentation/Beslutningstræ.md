# Beslutningstræ for Cirkulær assistent

Dette dokument beskriver det samlede beslutningstræ, som appen bruger i MVP/testversionen. Formålet er at sikre, at en genstand først vurderes som ressource, før den vurderes som affald.

## Grundprincip

```text
Tag/upload billede
  -> AI- eller testidentifikation
  -> supplerende spørgsmål
  -> beslutningsmotor
  -> anbefaling og næste handling
```

Den cirkulære prioritering er:

```text
Reparér -> Rens/klargør -> Sælg -> Bortgiv -> Affald
```

`Affald` må kun anbefales, når de øvrige realistiske muligheder er kontrolleret.

## 1. Foto

Brugeren kan:

- tage et billede med telefonens kamera
- uploade et eksisterende billede

Billedet vises straks i appen. Browseren komprimerer billedet før upload til backend, så store mobilfotos ikke gør flowet unødigt tungt.

## 2. Identifikation

Backend-endpointet `/api/analyze` modtager billedet.

Hvis `OPENAI_API_KEY` er sat:

- bruges OpenAI-billedanalyse
- svaret normaliseres til et struktureret objekt
- usikkerheder skal fremgå eksplicit

Hvis `OPENAI_API_KEY` ikke er sat:

- bruges lokal testanalyse
- billedet kan stadig bruges i flowet
- resultatet markeres tydeligt som testversion

Identifikationen kan indeholde:

- genstandsnavn
- kategori
- underkategori
- producent/mærke
- model
- materialer
- synlige skader
- estimeret stand
- generel affaldsfraktion
- vurderingssikkerhed
- usikkerhedsnoter

AI må ikke præsentere usikre antagelser som fakta.

## 3. Supplerende spørgsmål

Appen stiller korte spørgsmål, der bruges af beslutningsmotoren:

- Hvorfor vil du af med genstanden?
- Kender du producenten?
- Virker genstanden?
- Hvor gammel virker den?
- Kendte fejl eller skader?
- Følger vigtigt tilbehør med?
- Har den batteri eller ledning? Kun relevant ved elektronik.
- Kan rensning eller klargøring løfte standen? Kun relevant ved møbler.

## 4. Hvorfor vil brugeren af med genstanden?

Beslutningstræet starter med brugerens årsag:

```text
Hvorfor vil du af med genstanden?
  ├─ Defekt / virker ikke
  ├─ Bruger den ikke længere
  ├─ Vil erstatte den
  ├─ Har ikke plads
  ├─ Vil give den videre
  └─ Mener den er affald
```

## 5. Defekt / virker ikke

```text
Defekt / virker ikke
  -> kontrollér reparation
     ├─ realistisk: Reparér
     └─ ikke realistisk: Affald
```

En defekt genstand, hvor reparation ikke er realistisk, skal som udgangspunkt behandles som affald. En senere version kan tilføje en særskilt gren for "sælg som defekt/reservedele", men den skal være eksplicit og ikke automatisk.

Reparation vurderes ud fra:

- fungerer den delvist eller slet ikke?
- er skaden alvorlig?
- er der tale om elektronik, værktøj eller andet reparerbart produkt?
- er der batteri eller sikkerhedskritiske komponenter?

Appen må ikke give farlige eller for sikre tekniske diagnoser.

## 6. Rens/klargør for møbler

For møbler er værdiskabelsen ofte ikke teknisk reparation, men rensning eller klargøring.

```text
Møbel identificeret
  -> virker eller ukendt funktion
  -> ingen alvorlig skade
  -> rensning/klargøring kan løfte standen
     ├─ ja: Rens/klargør
     └─ nej: fortsæt til Sælg/Bortgiv
```

Rens/klargør kan dække:

- støvsugning
- tekstilrens
- pletbehandling
- aftørring
- efterspænding
- korrekt samling
- bedre præsentation før salg eller bortgivelse

Rens/klargør ligger før salg, fordi det kan øge genstandens værdi og sandsynligheden for videre brug.

## 7. Sælg

```text
Sælg
  -> identificér producent
  -> tjek producentordninger
  -> vurder almindeligt salg
  -> vis relevante salgsmuligheder
```

Salg er relevant når:

- genstanden virker
- skaden ikke er alvorlig
- der er restværdi
- tilbehør er komplet eller ikke relevant
- genstanden kan få højere værdi efter rens/klargøring

Resultatet kan vise:

- almindeligt privat salg
- hurtigt salg
- producentordning, hvis relevant
- prototypeestimat for økonomi og CO2

## 8. Producentordninger

IKEA må ikke hardcodes i beslutningsmotoren. Appen bruger derfor et generelt modul:

```text
producer_programs.py
```

Modulet kan rumme producenters:

- reparation
- reservedele
- buy-back/gensalg
- trade-in
- take-back
- refurbishment

Flowet er:

```text
Producent identificeret
  -> slå op i producentordninger
     ├─ ordning fundet: vurder relevans
     └─ ingen ordning: fortsæt almindeligt salg/bortgivelse
```

## 9. IKEA Gensalg som første integration

IKEA Gensalg er første konkrete producentordning.

```text
Producent = IKEA
  -> IKEA-produkt identificeret
  -> vurder potentiel relevans for IKEA Gensalg
     ├─ sandsynligvis relevant: vis IKEA Gensalg som salgsmulighed
     ├─ mulig: vis med forbehold
     └─ ikke oplagt: fortsæt almindelig salgsvurdering
```

Appen kontrollerer forsigtigt:

- originalt IKEA-produkt
- god/salgbar stand
- rent og uændret
- komplet og fuldt funktionelt
- mulig omfattet produktkategori

Appen må kun sige, at varen potentielt kan være relevant. Endelig godkendelse og pris afgøres af IKEA.

## 10. Bortgiv

```text
Bortgiv
  -> relevant hvis genstanden stadig kan bruges
  -> særligt relevant hvis salgspris eller salgsindsats er lav
```

Bortgivelse kan senere kobles til:

- genbrugsbutikker
- NGO'er
- bytteområder
- lokale gratisgrupper
- kommunale genbrugsordninger
- materialebanker

## 11. Affald som sidste mulighed

```text
Før affald:
  -> Kan den repareres?
  -> Kan den renses/klargøres?
  -> Kan den sælges?
  -> Kan den bortgives?

Kun hvis alle er nej:
  -> Affald/sortering
```

Når `Affald` er den mest realistiske anbefaling, rangeres alternativerne sådan i resultatskærmen:

```text
Affald -> Bortgiv -> Sælg
```

Det viser brugeren de mest realistiske tilbageværende valg først. `Sælg` placeres sidst i denne case, fordi en defekt/ikke-reparerbar genstand normalt har lavere salgssandsynlighed end bortgivelse.
Affaldsresultatet viser kun generel dansk vejledning i MVP'en.
Kommunespecifik vejledning må først vises, når reglen kommer fra en verificeret datakilde.

Affaldsvurdering kan omfatte:

- affaldsfraktion
- genbrugsplads eller beholder
- batterier
- elektronik
- farlige komponenter
- særlige afleveringskrav

## 12. Resultatskærm

Resultatet viser:

- identificeret genstand
- anbefalet handling
- kort begrundelse
- beslutningsvej
- kontrol af cirkulære muligheder
- økonomi-estimat
- CO2-estimat
- producentordning, hvis relevant
- generel affaldsfraktion, hvis genstanden ender som affald
- kildeklarhed og forbehold

## 13. Hallucinationsbeskyttelse

Appen skal skelne mellem:

- AI-vurdering
- testvurdering
- regelbaseret beslutning
- verificeret ekstern information

Regler:

- AI må ikke opfinde kommunale affaldsregler.
- AI må ikke gætte mærke/model som fakta.
- Producentordninger skal komme fra registry eller verificeret kilde.
- Affaldsregler skal senere komme fra kommune/postnummer/dataleverandør.
- Sikkerhedskritiske råd skal være forsigtige og kildebaserede.

## 14. MVP-outputstruktur

Et resultat kan indeholde:

```json
{
  "object_name": "Sofa",
  "recommended_action": "clean",
  "decision_path": ["Bruger den ikke længere", "Rens/klargør", "Vurder salg"],
  "confidence": "Middel",
  "reasoning": "Rensning kan skabe værdi før salg eller bortgivelse.",
  "checks": [],
  "options": [],
  "impact": {
    "economy": "lav udgift / højere værdi",
    "co2_saving": "5-20 kg CO2e"
  },
  "producer_program": {},
  "waste": {}
}
```

## 15. Fremtidige datakilder

Senere bør træet kobles til:

- kommunale affaldsregler
- postnummer/kommuneopslag
- genbrugspladser og åbningstider
- producentordninger
- reservedelsdatabaser
- reparationsmanualer
- Repair Café og reparatører
- markedspriser og brugtannoncer
- donationssteder
- Digital Product Passport
- GTIN/EAN
- CO2- og materialedatabaser



