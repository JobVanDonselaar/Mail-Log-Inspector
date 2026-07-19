# Mail Log Inspector Migration

## Huidige situatie

Mail Log Inspector is een zelfstandige WPF-app met eigen Core-, Storage- en testprojecten. De actieve gegevensbron is SMTP.com CSV/ZIP, handmatig of via Gmail-sync.

## Niet meer actief

- De oude toolbox-app en updaterwiring horen niet bij deze repo.
- De ongebruikte Outlook source-only implementatie is verwijderd.
- Databasewijzigingen worden niet handmatig uitgevoerd.

## Database-upgrades

De EXE bouwt bij een incompatibel schema een tijdelijke workspace vanuit het bronarchief. Alleen een volledig gevalideerde workspace wordt actief; een fout of annulering laat de vorige database beschikbaar.