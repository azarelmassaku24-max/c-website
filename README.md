# C# Quest — Programming Academy

Een gamified, interactieve C#-academy gebaseerd op de cursus "Programmeren Basis". Je schrijft echte C#-code in de browser; die wordt écht gecompileerd (Roslyn) en uitgevoerd in een afgeschermd proces.

## Vereisten
- .NET SDK 10 (`dotnet --version`)
- Een moderne browser (Edge/Chrome/Firefox)
- Internet is niet nodig bij gebruik (Monaco, xterm, Preact zitten lokaal in `src/web/vendor`)

## Opstarten
```bash
cd src/server
dotnet run
```
Open daarna http://localhost:5080. Voortgang wordt bewaard in `data/progress/default.json` én in de browser (localStorage).

## Structuur
| Map | Inhoud |
|---|---|
| `src/server` | ASP.NET Core backend: compiler (`Execution/`), beoordeling (`Grading/`), feedback-vertaling (`Feedback/`), curriculum-lader (`Curriculum/`), API (`Api/`), voortgangsopslag (`Progress/`) |
| `src/web` | Front-end (Preact + htm, zonder build-stap): `js/pages`, `js/features` (lab, editor, debugger, terminal, lesson), `js/progress` (XP, engine, unlocks, aanbevelingen), `js/achievements`, `css` |
| `curriculum` | **Leerinhoud, los van de code.** `curriculum.json` (levels, skill tree, rangen) + `levels/<map>/*.quest` (lessen en oefeningen) |
| `tests` | xUnit-testproject (nog uit te breiden) |
| `data` | Voortgang per profiel |

## Code-uitvoering en veiligheid
1. Roslyn compileert de code echt; fouten worden naar begrijpelijk Nederlands vertaald (`Feedback/DiagnosticTranslator.cs`).
2. Een allowlist-analyse (`SecurityAnalyzer`) weigert niet-toegelaten API's (Process, netwerk, reflectie, unsafe, …).
3. De code draait in een **apart `dotnet`-proces** met opgeschoonde omgevingsvariabelen, eigen werkmap, padbewaking (`QPath.Guard`), wandklok-/CPU-/outputlimieten, GC-heaplimiet en op Windows een **Job Object** (geheugen, 1 proces, geen UI).
4. Culture standaard `nl-BE` (zoals in de cursus: `3,5`). Tests gebruiken een vaste `Random`-seed en slaan `Thread.Sleep` over.

> Beperking: op Windows/Linux zonder container is dit proces-isolatie, geen volledige sandbox. Voor publieke hosting: draai de server in een Docker-container zonder netwerk met read-only bestandssysteem (nog niet meegeleverd).

## Nieuwe leerinhoud toevoegen
Maak in `curriculum/levels/<level-map>/` een `.quest`-bestand (zie bestaande bestanden als voorbeeld) en/of voeg een level toe aan `curriculum.json`. De server herlaadt automatisch.

Secties: `#! lesson`/`boss`, `#! begrijpen`, `#! voorbeeld`, `#! voorspel`, `#! oefening` (+ `#! starter`, `#! hint` ×3, `#! oplossing`, `#! uitleg`, `#! regel`, `#! test` met `#! invoer`/`#! verwacht`/`#! verwacht-bestand`/`#! zonder-bestand`). Een testsoort `soort=code` roept methods van de leerling rechtstreeks aan met `Check.Equal(...)`.

## Kwaliteitscontrole van de leerinhoud
```bash
cd src/server
dotnet run -- verify          # alles
dotnet run -- verify L06      # één level
dotnet run -- selftest        # compileer-/sandboxpipeline
```
`verify` bewijst dat elke modeloplossing al haar tests haalt en dat de **startcode nooit slaagt** (foute code wordt dus niet goedgekeurd).

## Status
Zie `PROGRESS.md`.
