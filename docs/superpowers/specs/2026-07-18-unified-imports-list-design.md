# Gecombineerde importlijst

## Doel

Vervang `Recente imports` en de technische Gmail-historielijst door een zakelijke, chronologische lijst `Imports`. Iedere handmatige import, Gmail-import en mislukte Gmail-poging staat maximaal eenmaal in de lijst.

## Presentatie

De lijst gebruikt de beschikbare breedte en bevat:

| Kolom | Inhoud |
| --- | --- |
| Datum | Gmail-ontvangstdatum of importdatum bij een handmatige import |
| Bron | `Gmail` of `Handmatig` |
| Bestand | Korte bestandsnaam; volledige naam via tooltip |
| Rapportperiode | Rapportdatum of begin- en einddatum |
| Mails | Totaal aantal geaccepteerde mails |
| Afgeleverd | Aantal afgeleverde mails |
| Bounce | Aantal bounces |
| Onderweg | Aantal mails zonder eindstatus |
| Status | Samengevat operationeel resultaat |

De status is een van:

- `Gereed`: Gmail-download en import zijn geslaagd en het bericht is permanent verwijderd.
- `Import gereed, verwijderen mislukt`: de data is veilig geïmporteerd, maar de Gmail-opruiming moet opnieuw worden geprobeerd.
- `Geïmporteerd`: handmatige import is geslaagd.
- `Download mislukt`.
- `Import mislukt`.

Foutdetails staan in een tooltip op de statuscel en worden niet in een brede permanente foutkolom getoond.

## Gegevenskoppeling

Voeg een nullable `source_hash` toe aan de operationele Gmail-historie. De Gmail-importrunner retourneert naast succes ook de bronhash van het gedownloade bestand. Deze stabiele hash koppelt Gmail-historie aan een rij in `imports`, ook wanneer import-ID's tijdens een EXE-beheerde rebuild veranderen.

De applicatie bouwt een gecombineerd presentatiemodel:

- import zonder Gmail-historie wordt `Handmatig`;
- Gmail-historie met gekoppelde import wordt één verrijkte Gmail-rij;
- mislukte Gmail-historie zonder import wordt een operationele foutregel zonder mailtotalen;
- een duplicaat wordt niet als tweede import getoond als de bronhash al aan een import gekoppeld is.

Voor bestaande historie probeert de EXE eenmalig en idempotent te koppelen op een exact unieke ZIP-bestandsnaam. Bij twijfel blijft de hash leeg; er wordt nooit willekeurig gekoppeld. Een oude succesvolle Gmail-regel zonder bewezen koppeling wordt niet als extra rij getoond; de importregel blijft leidend. Een mislukte Gmail-regel blijft altijd zichtbaar. Gecontroleerde duplicaten krijgen geen eigen rij.

## Layout

- Titel wordt `Imports`.
- De aparte Gmail-historietabel verdwijnt.
- De tegel `Sync` verdwijnt volledig uit de normale hoofdinterface.
- Automatische synchronisatie blijft met de opgeslagen instellingen op de achtergrond werken.
- De gecombineerde lijst gebruikt de vrijgekomen ruimte en behoudt de vernieuwknop.
- Sortering is standaard nieuwste datum eerst.
- Getallen worden numeriek sorteerbaar gemaakt.

## Adminmodus

Bij normaal starten wordt direct de gewone hoofdinterface geopend. Gmail-instellingen zijn daar niet zichtbaar.

Starten met:

```text
MailLogInspector.exe /admin
```

opent eerst een apart modaal instellingenvenster. Dit venster bevat:

- methode;
- Gmail-adres;
- Gmail app-wachtwoord;
- automatisch synchroniseren, vast iedere 15 minuten;
- sluiten naar systeemvak;
- verbinding testen;
- opslaan;
- annuleren.

Na `Opslaan` worden de instellingen veilig opgeslagen, wordt het dialoogvenster gesloten en start de normale hoofdinterface. `Annuleren` sluit de applicatie zonder wijzigingen en zonder de hoofdinterface te openen.

Het opgeslagen app-wachtwoord wordt nooit leesbaar teruggezet in het invoerveld. Een leeg wachtwoordveld behoudt bij opslaan de bestaande beveiligde waarde; alleen een nieuw ingevoerd wachtwoord vervangt deze.

## Bestaande instantie

De single-instance-aansturing krijgt naast `open` ook een `admin`-actie:

- normaal opnieuw starten activeert het bestaande hoofdvenster;
- opnieuw starten met `/admin` opent het adminvenster boven de bestaande instantie;
- er wordt nooit een tweede hoofdproces gestart;
- annuleren van dit adminvenster sluit alleen het venster en niet de reeds actieve applicatie.

## Fouten en lege waarden

- Ontbrekende rapportperiode of tellingen worden als `-` getoond.
- Een mislukte Gmail-download blijft zichtbaar, ook zonder importrecord.
- De volledige fouttekst is alleen via tooltip zichtbaar.
- Oude succesvolle Gmail-records zonder betrouwbare koppeling worden niet naast een importregel gedupliceerd.

## Testen

- Gmail-succes en bijbehorende import leveren precies één rij op.
- Handmatige import wordt als `Handmatig` getoond.
- Download- en importfouten blijven zonder tellingen zichtbaar.
- Verwijderfout toont de juiste status en fouttooltip.
- Duplicaten leveren geen tweede importregel op.
- Bestaande historie wordt alleen bij een unieke bestandsmatch gekoppeld.
- Datum- en getalkolommen sorteren op hun werkelijke waarden.
- De losse Gmail-historietabel en titel `Recente imports` zijn uit XAML verwijderd.
- Normaal starten toont nergens Gmail-instellingen.
- `/admin` toont het instellingenvenster vóór de eerste hoofdinterface.
- opslaan start daarna de hoofdinterface en annuleren beëindigt de eerste instantie.
- `/admin` bij een bestaande instantie opent het instellingenvenster in die instantie.
- een leeg app-wachtwoordveld overschrijft het opgeslagen geheim niet.
- De app-versie wordt verhoogd.
