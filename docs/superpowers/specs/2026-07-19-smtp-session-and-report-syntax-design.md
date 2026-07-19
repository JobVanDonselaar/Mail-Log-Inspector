# SMTP.com portalsessie en rapportsyntax

## Doel

Mail Log Inspector moet een bestaande SMTP.com-portaalsessie zo lang mogelijk
hergebruiken en beheerders een veilige manier geven om de naamopbouw van het
dagrapport aan te passen.

Versie voor deze wijziging: `0.192`.

## Portalsessie

Er komt geen periodieke keepalive of automatische refresh.

De bestaande WebView2-gebruikersmap blijft persistent:

`%LOCALAPPDATA%\Mail Log Inspector\WebView2\SmtpPortal`

Bij iedere directe synchronisatie:

1. opent de app `https://my.smtp.com/reporting?tab=reports`;
2. controleert de app eerst of `My reports` al beschikbaar is;
3. gebruikt de app de bestaande sessie direct wanneer die nog geldig is;
4. meldt de app alleen opnieuw aan wanneer SMTP.com het login- of MFA-scherm
   toont;
5. downloadt de app daarna het benodigde rapport.

Een refresh iedere 30 minuten is bewust niet opgenomen. De portal blijft in de
praktijk lang ingelogd, terwijl periodiek verversen onnodige achtergrondactiviteit
en portalverzoeken veroorzaakt en geen garantie geeft tegen een absolute
sessievervaldatum.

De operationele configuratie bewaart `last_successful_portal_use_at_utc`. Onder
`/admin` wordt dit getoond als:

`Portaalsessie laatst succesvol gebruikt: dd-MM-yyyy HH:mm`

Dit tijdstip wordt alleen bijgewerkt nadat `My reports` succesvol is gelezen.
Het wijzigen hiervan raakt de productiedatabase niet.

## Standaardsyntax

De bestaande exacte naamopbouw blijft standaard actief:

`NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)`

De placeholders betekenen:

- `{start}`: begindatum in `yyyy-MM-dd`;
- `{end}`: einddatum in `yyyy-MM-dd`.

Voorbeeld:

`NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)`

Los van de naamopbouw blijft gelden:

- status is `Ready`;
- de einddatum ligt na de begindatum;
- alleen pagina 1 wordt bekeken;
- het geselecteerde rapport valt binnen de bestaande synchronisatieperiode.

## Aangepaste syntax

Onder het SMTP.com-blok in `/admin` komen twee keuzerondjes:

- `Standaardsyntax`;
- `Aangepaste syntax`.

De opties zijn onderling uitsluitend. Bij `Standaardsyntax` wordt de vaste
standaardtekst read-only getoond. Bij `Aangepaste syntax` wordt een tekstveld
actief.

Een aangepaste syntax:

- bevat `{start}` exact één keer;
- bevat `{end}` exact één keer;
- gebruikt voor beide datums altijd `yyyy-MM-dd`;
- behandelt alle overige tekens en spaties letterlijk;
- is geen reguliere expressie;
- wordt vóór opslaan gevalideerd.

De interface toont onder het veld:

- een korte uitleg van beide placeholders;
- een voorbeeld met twee concrete datums;
- een zakelijke validatiefout wanneer de syntax niet geldig is.

Hierdoor kan bijvoorbeeld alleen de klantnaam of vaste rapporttekst worden
aangepast zonder dat een beheerder regex hoeft te kennen.

## Opslag

`smtp_portal_config` krijgt transactioneel en idempotent:

- `use_default_report_syntax INTEGER NOT NULL DEFAULT 1`;
- `custom_report_syntax TEXT NULL`;
- `last_successful_portal_use_at_utc TEXT NULL`.

Bestaande installaties blijven automatisch de standaardsyntax gebruiken.
Wachtwoorden, MFA-secrets en cookies veranderen niet.

## Componenten

- `SmtpPortalReportNameSyntax` bevat standaardtekst, validatie, voorbeeldopbouw
  en omzetting naar een verankerde regex.
- `SmtpPortalReportMatcher` gebruikt de effectieve syntax en blijft status en
  datumvolgorde afzonderlijk controleren.
- `SmtpPortalOperationalStore` bewaart de nieuwe instellingen en het laatste
  succesvolle portalgebruik.
- `AdminSettingsWindow` toont de syntaxopties compact binnen het bestaande
  venster zonder ScrollViewer.
- Zowel proefdownload als productie-download gebruikt dezelfde effectieve
  syntax.

## Foutafhandeling

- Een ongeldige aangepaste syntax kan niet worden opgeslagen.
- Een syntax zonder beide placeholders wordt afgewezen.
- Geen overeenkomend `Ready`-rapport behoudt de bestaande fallback- of
  foutafhandeling.
- Een verlopen portalsessie start de bestaande login- en MFA-flow.
- Een mislukte portalactie wijzigt het tijdstip van het laatste succesvolle
  portalgebruik niet.

## Tests

- standaardsyntax accepteert het huidige dagrapport;
- aangepaste syntax accepteert een geldig aangepast rapport;
- ontbrekende, dubbele of onbekende placeholders worden afgewezen;
- letterlijke haakjes, plustekens en spaties worden correct behandeld;
- status anders dan `Ready` blijft ongeldig;
- proefdownload en productie-download gebruiken dezelfde syntax;
- bestaande configuratiedatabases migreren naar standaardsyntax;
- laatste succesvolle portalgebruik wordt correct opgeslagen en geladen;
- `/admin` bevat beide opties, uitleg en voorbeeld zonder ScrollViewer;
- Help, README en SMTP-downloaddocumentatie beschrijven de nieuwe werking;
- volledige testset, Debug-build, versiecontrole en `git diff --check`.
