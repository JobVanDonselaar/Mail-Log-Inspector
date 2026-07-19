# Roadmap

## Productdoel

Mail Log Inspector moet snel openen, compact blijven en vooral sterk zijn in:

- importeren van mail log CSV/ZIP bestanden
- snel zoeken op periode, afzender, ontvanger en domein
- praktische analyse over recente periodes

De app is geen generieke BI-omgeving. Focus ligt op snelle operationele inzichten over ongeveer 30 dagen.

## Huidige status

Klaar:

- standalone repo los van oude Mail Log Toolbox
- eigen productiemap `C:\Apps\Mail Log Inspector`
- one-file publish workflow via `scripts\\Publish-MailLogInspector.ps1`
- eigen SQLite-database
- handmatige CSV/ZIP import via de app
- zoeken met resultatenoverzicht
- basis analyse-tab
- versie in titelbalk
- directe SMTP.com-download met MFA, sessiebehoud, validatie en IMAP-fallback
- Gmail, Microsoft 365 en eigen IMAP met verplichte TLS
- dagelijkse planning vanaf 01:00 met herhaling per 15 minuten en oudste ontbrekende dag eerst
- annuleerbare synchronisatie en databasewrites
- transactionele retentie en analyse-aggregaten
- begrensde ZIP-, CSV- en downloadverwerking

Bewust uitgangspunt:

- geen handmatige DB-migraties buiten de app
- bij structurele schemawijziging liever DB opnieuw opbouwen via import
- AGENTS.md is de leidende repo-regelset voor Codex en andere agents
- publish gebeurt expliciet via het script of de batch-wrapper, niet handmatig

## Fase 1: stabiele basis

Doel:

- app moet voorspelbaar starten
- schone database moet automatisch opnieuw opgebouwd kunnen worden
- import moet duidelijk voortgang tonen

Nog te doen:

- publish-output verder opschonen
- importstatus visueel verder aanscherpen bij grote bestanden
- controle dat zoeken en analyse netjes werken na volledig lege herimport

## Fase 2: strakker ontwerp

Doel:

- professionelere, rustigere interface
- tabs `Zoeken`, `Analyse`, `Beheer` bovenaan met duidelijke folder-tab uitstraling

Nog te doen:

- huidige redesign mockups doorvoeren in WPF/XAML
- header rustiger maken, minder losse statusblokken
- consistentere spacing, knoppen en cards
- detailpaneel en resultatenweergave verder aanscherpen

## Fase 3: zoeksnelheid en compacte opslag

Doel:

- snelle zoekacties op grote datasets
- data-opslag gericht op echte gebruiksvragen

Richting:

- optimaliseren voor zoeken op afzenderdomein en ontvangers
- alleen opslaan wat nodig is voor operationeel inzicht
- waar mogelijk compacte representatie van status/tijdlijn

Onderzoekspunten:

- compacte opslag van domeinen en adressen
- voorbereid zijn op veel zoekvragen op afzenderdomein
- bepalen welke velden echt nodig blijven voor analyse

## Fase 4: analyse die echt nuttig is

Doel:

- directe inzichten zonder zware dashboardlaag

Gewenste onderdelen:

- totalen: send, afgeleverd, onderweg, bounce
- topdomeinen
- probleemlijsten
- drill-down van analyse naar zoekresultaten

Mogelijke vervolgstap:

- zwaardere trend- of dashboardanalyse eventueel in aparte EXE of aparte modus

## Bewaakte uitgangspunten

- snelheid van zoeken is belangrijker dan maximale import-snelheid
- compacte DB is gewenst, maar niet ten koste van bruikbare analyse
- import moet betrouwbaar en begrijpelijk aanvoelen
- gebruiker moet alles vanuit de app kunnen doen

## Praktische volgende stappen

1. complete herimport testen vanaf lege DB
2. controleren dat zoeken en analyse na herimport functioneel juist zijn
3. redesign van de drie hoofdtabs implementeren
4. daarna pas verdere datamodel-optimalisatie voor snelheid en compactheid
