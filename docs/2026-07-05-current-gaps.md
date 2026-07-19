# Mail Log Inspector Current Gaps

## Bestaand GUI-voorstel

Het ontwerpvoorstel voor de GUI staat al hier:

- [docs/design/2026-07-03-mail-log-inspector-v1-design.md](/Y:/Mail Log Inspector/docs/design/2026-07-03-mail-log-inspector-v1-design.md)

Dat document beschrijft de productrichting, het compacte datamodel en de hoofdschermen `Zoeken`, `Analyse` en `Beheer`.

## Wat er nu functioneel al is

Op basis van de huidige app-code zijn deze onderdelen al aanwezig:

- aparte app `Mail Log Inspector`
- eigen SQLite database
- handmatige CSV- en ZIP-import vanuit de app
- drag-and-drop import in `Beheer`
- zoekscherm met datum, afzender, ontvanger en resultaatlimiet
- statusfilter in de resultatenkop
- detailpaneel naast zoekresultaten
- Excel-export van zoekresultaten
- analyse met totalen, top afzenderdomeinen, top ontvangerdomeinen, bounce-lijst en onderweg-lijst
- drill-down van analyse naar zoeken
- one-file publish script
- versie in titelbalk

## Wat nog mist of nog niet strak genoeg is

### 1. GUI redesign nog niet doorgevoerd

De app gebruikt nu nog een functionele maar vrij ruwe WPF-indeling. Dit ontbreekt nog:

- tabs bovenaan met duidelijke folder-tab uitstraling
- rustiger en professioneler headerontwerp
- minder losse statusblokken bovenaan
- consistentere spacing, marges en uitlijning
- strakkere knoppen, kaarten en grid-opmaak
- visueel nettere detailweergave

Kort: het ontwerpdoel is al beschreven, maar de uiteindelijke redesign is nog niet doorgebouwd in XAML.

### 2. Analyse is bruikbaar maar nog niet compleet volgens v1-ontwerp

In het design staan ook analyse-onderdelen die ik nu niet terugzie in de UI:

- gemiddelde doorlooptijd
- p95 doorlooptijd
- expliciete slow-delivery lijst of vergelijkbare vertraagd-lijst

De huidige analyse geeft vooral aantallen en probleemlijsten, maar nog niet alle timing-inzichten uit het ontwerp.

### 3. Importfeedback kan nog duidelijker

De app heeft al voortgangstekst en een progress bar, maar dit kan nog beter voor grote imports:

- duidelijker fase-indicatie tijdens grote CSV- of ZIP-import
- beter onderscheid tussen lezen, verwerken en opslaan
- duidelijkere foutmeldingen bij kapotte bestanden of onverwachte kolommen
- beter zichtbaar eindresultaat per importactie

### 4. Zoekscherm kan nog verder aangescherpt worden

Functioneel is zoeken aanwezig, maar dit zijn nog logische vervolgstappen:

- definitieve redesign van het zoekscherm
- explicieter onderscheid tussen zoekfilters en resultaatfilters
- strakkere standaard leegstand bij openen, zodat de app direct licht aanvoelt
- verdere verfijning van resultatenweergave en detailnavigatie

### 5. Beheer-tab kan nog informatiever

De basis staat, maar beheer kan nog verder worden afgemaakt:

- duidelijkere databasegezondheid en database-omvang
- duidelijkere importgeschiedenis
- strakkere rebuild-feedback
- beter zicht op wat opnieuw opbouwen precies gaat doen

### 6. Datamodel-optimalisatie is nog niet af

De huidige basis werkt, maar de compactheids- en snelheidsstrategie is nog niet definitief afgerond:

- verdere optimalisatie voor zoeken op afzenderdomein
- controleren of timingvelden altijd nodig zijn of slimmer later berekend moeten worden
- opnieuw beoordelen welke velden echt bewaard moeten blijven
- compact houden zonder analyse of supportwaarde te verliezen

## Praktische conclusie

De kern van v1 staat al:

- import
- database
- zoeken
- analyse
- export

Wat nu vooral nog openstaat:

1. de professionele GUI-afwerking
2. sterkere importfeedback
3. extra analyse-inzichten rond doorlooptijd
4. verdere opslag- en zoekoptimalisatie voor grotere datasets
