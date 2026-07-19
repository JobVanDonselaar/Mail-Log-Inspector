# Synchronisatiebron en SMTP.com-productie-import

## Doel

Mail Log Inspector krijgt één centrale keuze voor de bron van dagelijkse SMTP.com-rapporten. Dezelfde keuze geldt voor automatisch synchroniseren en voor `Sync nu`.

De beschikbare modi zijn:

1. `Direct downloaden, bij fout Gmail`
2. `Alleen Gmail`
3. `Alleen direct downloaden`

Bestaande installaties gebruiken standaard `Alleen Gmail`, zodat een upgrade het huidige productiegedrag niet onverwacht verandert.

## Nieuwe en lege database

Wanneer de actieve database ontbreekt, maakt de EXE de database en benodigde operationele tabellen aan via de bestaande initialisatie. GitHub Copilot of een los script past de productiedatabase niet rechtstreeks aan.

Een database zonder importhistorie vormt een nieuwe basis:

- alleen het nieuwste geldige beschikbare dagrapport wordt gedownload en geïmporteerd;
- oudere rapporten worden niet automatisch teruggevuld;
- deze regel geldt voor SMTP.com direct en voor Gmail, inclusief Gmail als fallback;
- na deze eerste import wordt de rapportdatum de basis voor toekomstige achterstandscontrole.

Bij latere synchronisaties worden alleen ontbrekende rapportdagen na deze basis tot en met gisteren verwerkt. Die dagen worden oudste eerst geïmporteerd.

## Architectuur

### ReportSyncCoordinator

Een nieuwe `ReportSyncCoordinator` wordt het enige startpunt voor:

- de automatische timer;
- synchronisatie na het opstarten;
- `Sync nu`.

De coordinator leest de ingestelde modus en roept de juiste bronservice aan. Hierdoor bevatten `MainWindow`, de timer en de knoppen geen eigen fallbacklogica.

### Gmail-bron

De bestaande `GmailReportSyncService` blijft de Gmail-berichten, downloadlinks en permanente verwijdering beheren.

De service krijgt een begrensde synchronisatieopdracht:

- `latestOnly` bij een lege importhistorie;
- ontbrekende dagen na de laatst geïmporteerde rapportdag bij normale werking.

De bestaande bescherming tegen dubbele import op basis van bronhash blijft actief.

### SMTP.com-bron

Een nieuwe productiegerichte SMTP.com-service hergebruikt:

- het blijvende WebView2-profiel;
- automatische login en MFA;
- rapportnaamherkenning;
- ZIP-download;
- ZIP-validatie;
- SHA-256-controle.

De bestaande `Proefdownload uitvoeren` blijft een aparte diagnosefunctie en roept de importservice nooit aan.

De productieflow:

1. opent uitsluitend pagina 1 van `My reports`;
2. gebruikt normaal de bestaande standaard van 10 rapporten per pagina;
3. schakelt alleen bij meer dan drie kalenderdagen achterstand naar 100 per pagina;
4. selecteert uitsluitend `Ready`-rapporten met `delivered + bounced + queue` en `raw_event_stream`;
5. negeert spamrapporten;
6. bepaalt de benodigde rapportdagen;
7. downloadt en importeert ontbrekende dagen oudste eerst;
8. gebruikt de bronhash als laatste bescherming tegen dubbele import.

Er wordt nooit naar pagina 2 genavigeerd. Als een benodigde dag niet op pagina 1 met maximaal 100 resultaten staat, wordt een zakelijke fout gemeld.

### Import runner

De ZIP-import wordt brononafhankelijk gemaakt. Gmail en SMTP.com direct gebruiken dezelfde import runner en dezelfde bestaande importservice. Handmatige import blijft dezelfde importservice gebruiken.

## Fallbackgedrag

In de modus `Direct downloaden, bij fout Gmail` wordt Gmail gestart wanneer SMTP.com direct:

- technisch faalt;
- niet kan aanmelden;
- geen passend `Ready`-rapport bevat;
- een ongeldig of beschadigd ZIP-bestand levert;
- een benodigde rapportdag niet op de toegestane eerste pagina bevat.

Als SMTP.com al één of meer ontbrekende dagen succesvol heeft geïmporteerd en daarna faalt, probeert Gmail alleen de resterende ontbrekende dagen te verwerken.

In de modus `Alleen direct downloaden` wordt Gmail nooit automatisch aangeroepen.

In de modus `Alleen Gmail` wordt de SMTP.com-productieflow nooit geopend.

## Planning

De eerste automatische synchronisatiepoging van iedere dag vindt plaats om `01:00` lokale tijd.

- Start de app vóór 01:00, dan wacht de automatische synchronisatie tot 01:00.
- Start de app om of na 01:00 en ontbreekt gisteren nog, dan wordt direct een poging uitgevoerd.
- Na een mislukte of nog niet beschikbare download wordt iedere vijftien minuten opnieuw geprobeerd.
- `Sync nu` blijft onafhankelijk van het tijdstip direct uitvoeren.

Zodra het rapport van gisteren succesvol is geïmporteerd, ongeacht de bron, wordt niet opnieuw automatisch gecontroleerd tot 01:00 op de volgende lokale kalenderdag.

De planning gebruikt brononafhankelijke synchronisatiehistorie en de laatst geïmporteerde rapportdag. Een geslaagde proefdownload telt niet als productie-import.

## Configuratie

Onder `/admin` komt boven de broninstellingen een keuzelijst `Synchronisatiebron`.

De bronkeuze wordt in de operationele configuratiedatabase opgeslagen, niet in de grote maildatabase. Een nieuwe brononafhankelijke operationele store beheert:

- geselecteerde synchronisatiemodus;
- laatste synchronisatiepoging;
- laatste succesvolle productiesynchronisatie;
- bronregistratie per geïmporteerde hash.

Gmail- en SMTP.com-instellingen blijven beide beschikbaar. Dat is nodig om fallback te kunnen activeren zonder de configuratie opnieuw in te voeren.

## Logging en importlijst

Iedere import krijgt precies één vast bronlabel:

- `SMTP.com direct`
- `Gmail`
- `Handmatig`

De bron wordt zichtbaar in:

- `Logs\mail-log-inspector.log`;
- voortgangs- en eindmeldingen;
- de gecombineerde importlijst onder Beheer.

De operationele database koppelt de bron aan de bronhash. Dit voorkomt een extra bronwaarde op iedere mailregel en houdt de grote database compact.

Voorbeelden:

```text
Bron=SMTP.com direct | Rapport=17-07-2026 | Import geslaagd
Bron=SMTP.com direct | Geen Ready-rapport | Fallback=Gmail
Bron=Gmail | Rapport=17-07-2026 | Import geslaagd
Bron=Handmatig | Bestand=report.zip | Import geslaagd
```

Als een hash al via een andere bron is geïmporteerd, blijft de oorspronkelijke importbron behouden. De nieuwe poging wordt apart als overgeslagen synchronisatiepoging gelogd.

## Fouten en veiligheid

- Wachtwoorden, MFA-secrets, TOTP-codes, cookies en downloadtokens worden nooit gelogd.
- Een directe download wordt pas geïmporteerd nadat ZIP- en CSV-validatie zijn geslaagd.
- Een mislukte bronpoging verwijdert geen bestaande data.
- Een gedeeltelijk geslaagde directe reeks wordt niet teruggedraaid; fallback verwerkt alleen wat nog ontbreekt.
- Database- en operationele schemawijzigingen worden uitsluitend idempotent door de EXE uitgevoerd.
- De bestaande proefdownload blijft buiten de productie-import.

## Testplan

- Configuratietests voor alle drie modi en standaard `Alleen Gmail`.
- Coordinatortests voor Gmail-only, direct-only en direct-met-fallback.
- Fallbacktests voor technische fout en ontbrekend `Ready`-rapport.
- Test dat direct-only Gmail nooit aanroept.
- Test dat Gmail-only WebView2 nooit opent.
- Lege-databasetest: alleen het nieuwste rapport wordt verwerkt.
- Lege-database-fallbacktest: Gmail verwerkt eveneens alleen het nieuwste rapport.
- Achterstandstest: ontbrekende dagen worden oudste eerst verwerkt.
- Gedeeltelijke-directtest: Gmail krijgt alleen resterende dagen.
- Hashtest voor een reeds geïmporteerd rapport.
- Planningstest: na succesvolle import van gisteren pas om 01:00 op de volgende lokale kalenderdag opnieuw controleren.
- Planningstest: vóór 01:00 geen automatische poging en om of na 01:00 wel.
- Planningstest: `Sync nu` blijft vóór 01:00 direct beschikbaar.
- Log- en importlijsttests voor `SMTP.com direct`, `Gmail` en `Handmatig`.
- Proefdownloadtest die bewijst dat de importservice niet wordt aangeroepen.
- Volledige testset, Debug-build en `git diff --check`.

## Versie en documentatie

De implementatie verhoogt de applicatieversie naar `0.189`. README, Help en de SMTP.com-downloaddocumentatie worden bijgewerkt. GitHub Copilot publiceert niet.

