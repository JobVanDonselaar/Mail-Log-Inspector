# SMTP.com rapportdownload

## Doel

Mail Log Inspector kan dagelijkse SMTP.com-rapporten rechtstreeks uit `My reports`, via IMAP of handmatig verwerken. Versie 0.197 gebruikt voor automatische synchronisatie één broncoördinator.

## Bronkeuze

Onder `MailLogInspector.exe /admin` kiest de beheerder:

- `Direct downloaden, bij fout IMAP`: SMTP.com direct is primair. De ingestelde IMAP-mailbox wordt gebruikt bij een technische fout of wanneer geen passend `Ready`-rapport beschikbaar is.
- `Alleen IMAP`: het rapport wordt via de ingestelde IMAP-mailbox gedownload, geïmporteerd en na succes permanent uit die mailbox verwijderd.
- `Alleen direct downloaden`: alleen het SMTP.com-portaal wordt gebruikt; fouten leiden niet tot IMAP-fallback.

`Sync nu` en `Nu synchroniseren` gebruiken dezelfde keuze en starten altijd onmiddellijk. De beheerdersactie initialiseert zo nodig een lege database en importeert dan alleen het nieuwste geldige rapport.

Een lopende synchronisatie kan met `Stop` worden geannuleerd. De annulering loopt door naar downloaden, CSV-import en SQLite-writes; een gedeeltelijke import wordt niet als succes geregistreerd.

## Automatische planning

- Eerste poging: dagelijks om `01:00` lokale tijd.
- Herhaling: iedere 15 minuten zolang het rapport van gisteren ontbreekt.
- Stopconditie: zodra de nieuwste geïmporteerde dag gelijk is aan gisteren, volgen die dag geen automatische pogingen meer.
- Bij een lege database of ontbrekende importhistorie wordt alleen het nieuwste geldige rapport geïmporteerd.
- Bij bestaande historie worden nieuwere ontbrekende rapportdagen oudste eerst verwerkt.
- De rapportdag wordt uit UTC-tijdstempels bepaald; de planning om 01:00 blijft lokale tijd gebruiken.

De EXE initialiseert een ontbrekende database zelf. Codex of een beheerder hoeft de SQLite-database daarvoor niet handmatig aan te maken of te wijzigen.

## SMTP.com-selectie

De portaalflow:

1. opent met WebView2 direct `https://my.smtp.com/reporting?tab=reports`;
2. hergebruikt een blijvend lokaal browserprofiel;
3. meldt zo nodig aan met gebruikersnaam, wachtwoord en lokaal gegenereerde MFA-code;
4. leest uitsluitend pagina 1 van `My reports`;
5. selecteert de benodigde geldige dagrapporten;
6. downloadt en valideert iedere ZIP;
7. vergelijkt SHA-256 met bestaande importhashes;
8. importeert nieuwe rapporten via de bestaande importservice.

Een rapport is alleen geldig wanneer:

- de naam begint met `NextGen_`;
- de periode exact `yyyy-MM-dd(00)_yyyy-MM-dd(00)` bevat;
- de naam `(delivered + bounced + queue)` bevat;
- de naam eindigt op `(raw_event_stream)`;
- de status `Ready` is.

Spamrapporten en andere streams worden genegeerd. De app klikt nooit op `Add CSV Report`, andere Reporting-tabs of paginering.

## Paginagrootte

Normaal blijft de portalinstelling ongemoeid, doorgaans `10 / page`. Alleen als de laatste succesvolle dagimport meer dan drie kalenderdagen oud is, kiest de app `100 / page`. De app gaat nooit verder dan pagina 1. Staat een benodigde dag niet binnen die eerste 100 regels, dan is handmatige actie nodig.

## IMAP-terugval

In de terugvalmodus wordt de ingestelde IMAP-mailbox geprobeerd wanneer:

- aanmelden, MFA, navigatie, DOM-herkenning, downloaden of ZIP-validatie van SMTP.com direct mislukt;
- geen passend `Ready`-rapport beschikbaar is;
- een benodigde dag op de eerste rapportpagina ontbreekt.

Een reeds direct geïmporteerde bronhash wordt via IMAP niet opnieuw geïmporteerd. De bronregistratie toont `Gmail` voor Gmail en `IMAP` voor Microsoft 365 of een eigen server.

## Handmatig downloaden en importeren

De algemene beheerdersinstellingen worden onafhankelijk van Gmail opgeslagen. `Nu synchroniseren` gebruikt de gekozen bron en roept de normale importservice aan. Bij een bestaande database worden ontbrekende dagen verwerkt; bij een lege database alleen het nieuwste rapport.


De beheerder kiest voor de mailbox `Gmail`, `Microsoft 365 / Outlook.com` of `Eigen IMAP-server`. De eerste twee gebruiken vaste IMAP- en TLS-instellingen. Een eigen server gebruikt direct SSL of verplichte STARTTLS; plaintext fallback is niet toegestaan. Google OAuth blijft uitsluitend voor Gmail beschikbaar; wachtwoorden worden DPAPI-versleuteld opgeslagen.
## Proefdownload en diagnose

De knoppen onder `/admin` blijven gescheiden van productie-import:

- `Proefdownload uitvoeren` downloadt en valideert alleen het nieuwste passende rapport.
- `Zichtbare diagnose` voert dezelfde veilige flow uit met een zichtbaar WebView2-venster.
- Proefbestanden staan onder `Incoming\SmtpPortalProbe`.
- De proef roept de importservice nooit aan en wijzigt de productiedatabase niet.

## Beveiliging

- Wachtwoord en MFA-secret worden met Windows DPAPI voor de huidige gebruiker versleuteld.
- De app kiest bij een cookiemelding uitsluitend `Reject All`.
- Het browserprofiel staat onder `%LOCALAPPDATA%\Mail Log Inspector\WebView2\SmtpPortal`.
- MFA wordt lokaal berekend voor de huidige 30-secondenstap, met maximaal één stap ervoor en erna.
- Logs bevatten geen wachtwoorden, MFA-secrets, codes, cookies, downloadtokens, DOM-inhoud of accountgegevens.
- Navigatie buiten `my.smtp.com` wordt geblokkeerd.
- ZIP-bestanden zijn begrensd op 512 MB en uitgepakte CSV-bestanden op 3 GB, met maximaal 100 ZIP-entries en compressieverhouding 200.
- Een ZIP bevat precies één niet-lege CSV. Header-only is een geldig nulrapport; volledig foutieve data wordt niet geïmporteerd.

## Logging en importhistorie

Iedere import wordt aangeduid als:

- `SMTP.com direct`;
- `Gmail`;
- `IMAP`;
- `Handmatig`;

Het bronlabel staat in het lokale log en in de gecombineerde importlijst op Dashboard. De operationele bronregistratie gebruikt de bronhash en wijzigt de productiedatabase niet.

## Technische verwijzingen

- Coördinatie: `src/MailLogInspector.App/ReportSyncCoordinator.cs`
- Planning: `src/MailLogInspector.App/ReportSyncSchedulePolicy.cs`
- Directe bron: `src/MailLogInspector.App/SmtpPortalReportSyncSource.cs`
- IMAP-bron: `src/MailLogInspector.App/GmailReportSyncSource.cs`
- Portaalbesturing: `src/MailLogInspector.App/SmtpPortalBrowserWindow.xaml.cs`
- Selectieregels: `src/MailLogInspector.App/SmtpPortalReportMatcher.cs`
- Proeforkestratie: `src/MailLogInspector.App/SmtpPortalProbeService.cs`
- Bronconfiguratie en -historie: `src/MailLogInspector.Storage/ReportSyncOperationalStore.cs`
