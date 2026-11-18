# Ontwikkelstatus (voor het hervatten van een sessie)

## Klaar en geverifieerd (`dotnet run -- verify` = alles OK)
- Backend: compiler, sandbox, beoordeling, feedbackvertaling, curriculum-lader, API, voortgangsopslag, WebSocket-terminal, IntelliSense.
- Front-end: alle 11 pagina's, Code Lab (3 zones), debugger/traceertabel, XP/levels/achievements/skill tree/spaced repetition/dagelijkse uitdaging.
- Leerinhoud (lessen met begrijpen → voorbeeld → voorspellen → oefenen → meesterschap, + boss):
  L01 (Deel 3–5), L02 (6), L03 (7), L04 (8), L05 (9), L06 (10), L07 (11), L08 (12), L09 (13), L10 (14, lessen 1–4 + boss), L11 (15, 4 lessen + boss).

## Nog te doen (in volgorde van prioriteit)
1. **L10**: les over `Persoon` + `D14uitlening` (optioneel extra).
2. **L12** Collections (Deel 16), **L13** Dictionaries (Deel 17), **L14** Interfaces (Deel 18 + de 3 demo-zips: Circle/Rectangle → Figure → IFigure; IComparer).
3. **PA** personenapplicatie (PA2–PA5, map `levels/PA-personen`, lessen met `vereist: L06/L09/L10/L12`), **VD** verdieping (overloading, optionele parameters, var, IDisposable, try/finally, eigen properties, GC).
4. **Kennisbank**: `curriculum/knowledge/*.md` (formaat: `## Titel` + `<!-- categorie: X | tags: a, b -->`). Foutmeldingen zijn al beschikbaar via DiagnosticTranslator.
5. Tot L11–PA/VD bestaan zijn die levels in `curriculum.json` leeg: ze tonen 0 lessen en blijven vergrendeld (Vrije modus in Settings ontgrendelt ze).
6. xUnit-tests in `tests/` (nu leeg), Docker-configuratie, volledige UI-regressietest met CDP-script (zie scratchpad `cdp/`).

## Bekende aandachtspunten
- Cursus-uitleg Deel 15 ontbreekt (`uitleg 15` = kopie van Deel 14): L11 steunt op oefeningen/oplossingen/verdieping van Deel 15.
- Deel 1–2 theorie zit niet in het materiaal: aanvullende uitleg is gemarkeerd (`aanvulling:`).
