# Importkwaliteit: vorige week als schaduw

## Doel

De tegel `Totalen` vergelijkt de nieuwste dagelijkse rapportimport met exact dezelfde rapportdag zeven dagen eerder. De huidige import staat als gekleurde staaf vooraan. De vorige week staat als een bredere, lichtgrijze schaduwstaaf erachter.

## Gedrag

- Gebruik de nieuwste import die als dagelijkse rapportimport geldt.
- Bepaal de referentiedatum als `rapportdatum - 7 dagen`.
- Gebruik de nieuwste dagelijkse import die exact op die referentiedatum eindigt, zodat dubbele rapportdownloads niet dubbel tellen.
- Negeer oudere gelijke weekdagen en grote vulimports.
- Toon onder iedere staaf `Vorige week <aantal>`.
- Toon `Geen gegevens vorige week` en geen schaduwstaaf als de exacte referentiedag ontbreekt.
- Gebruik voor iedere grafiek een schaal die zowel de huidige waarde als de vorige-weekwaarde omvat.
- Verwijder de hardcoded badge `Geen vergelijkbasis`.

## Presentatie

- Titel: `Totalen: laatste import vs vorige week`.
- Vorige week: brede neutraalgrijze staaf achteraan.
- Laatste import: smallere bestaande blauwe, groene of rode staaf vooraan.
- De actuele waarde bovenaan blijft ongewijzigd.

## Techniek

De vergelijking gebruikt uitsluitend de bestaande lijst met recente importstatistieken. Er komt geen databasewijziging en geen extra query per tegel.
