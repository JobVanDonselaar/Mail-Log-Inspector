# Zakelijke Help-tab

## Doel

De Help-tab wordt een compacte, zakelijke naslag voor Mail Log Inspector.
De gebruiker moet zonder screenshots kunnen begrijpen waarvoor de applicatie
bedoeld is, wat iedere hoofdtab doet en welke instellingen invloed hebben op
import, synchronisatie, zoeken, analyse en opslag.

## Vormgeving

- Een rustige eenkolomsindeling met een maximale leesbreedte.
- Geen screenshots, afbeeldingen of onderschriften bij afbeeldingen.
- Een korte introductie bovenaan.
- Witte secties met een duidelijke titel en compacte bulletlijsten.
- Korte zinnen, vaste terminologie en geen marketingtekst.
- Belangrijke waarschuwingen worden in een terughoudend gekleurd kader gezet.
- De knop `Beheerdersinstellingen openen` blijft onderaan beschikbaar.

## Inhoud

De Help-tab bevat de volgende secties, in deze volgorde:

1. **Doel en snel beginnen**
   - doel van Mail Log Inspector;
   - normale dagelijkse route door de applicatie;
   - automatische analyse van gisteren bij het opstarten.
2. **Zoeken**
   - periode, afzender, ontvanger en resultaatlimiet;
   - adres- en domeinfilters;
   - statusfilter, groepering, uitklappen, `Meer laden` en sortering;
   - betekenis van geaccepteerd, afgeleverd, onderweg, bounce en duur.
3. **Domeinanalyse en Excel**
   - voorwaarden voor domeinanalyse;
   - betekenis van trend, aflevertijdverdeling en bounce-oorzaken;
   - wat de Excel-export bevat en welk resultaatbereik wordt geëxporteerd.
4. **Analyse**
   - periode- en domeinfilters;
   - KPI's, domeinranglijsten, SMTP-responsen en topselectie;
   - dubbelklikken om een domein in Zoeken te openen.
5. **Dashboard**
   - databasegegevens, importkwaliteit, grafieken en importlijst;
   - handmatige CSV/ZIP-import;
   - synchronisatie, bronlabels, acties, archieven en opslag.
6. **Synchronisatie en beheerdersinstellingen**
   - bronkeuze en fallbackgedrag;
   - automatische poging om 01:00 en herhaling per 15 minuten;
   - SMTP.com-proefdownload en diagnose;
   - IMAP-providers en versleutelde opslag van geheimen;
   - standaard- en aangepaste rapportsyntax.
7. **Database en maandarchieven**
   - actieve retentieperiode;
   - automatische archivering;
   - openen en sluiten van een maandarchief;
   - verbod op handmatige databasewijzigingen.
8. **Systeemvak en afsluiten**
   - sluiten naar systeemvak;
   - bestaande instantie openen;
   - applicatie volledig afsluiten.
9. **Veelvoorkomende situaties**
   - geen resultaten;
   - rapport nog niet beschikbaar;
   - synchronisatie- of importfout;
   - domeinanalyse niet beschikbaar;
   - waar het lokale log staat.

## Technische grenzen

- Alleen de Help-tab in `MainWindow.xaml` wordt inhoudelijk en visueel gewijzigd.
- De bestaande Help-knop voor `/admin` en de eventhandler blijven behouden.
- De afbeeldingsbestanden onder `Assets/Help` mogen uit het project verdwijnen
  wanneer ze nergens anders worden gebruikt.
- Functionele applicatielogica en databases worden niet gewijzigd.
- De applicatieversie wordt verhoogd naar `0.194`.

## Acceptatie

- De Help-tab bevat geen `Image`-elementen of verwijzingen naar `Assets/Help`.
- Alle bovengenoemde secties en kernbegrippen zijn aanwezig.
- De tekst is bruikbaar als naslag zonder externe documentatie.
- De Help-tab blijft goed leesbaar bij de bestaande vensterbreedte.
- Bestaande tests slagen en een nieuwe layouttest bewaakt de structuur.
- De Debug-build slaagt zonder fouten of waarschuwingen.
