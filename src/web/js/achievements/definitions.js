import { isLessonComplete, isLevelComplete } from '../progress/unlocks.js';
import { currentStreak } from '../progress/stats.js';

/**
 * Prestaties. `check(p, ctx)` krijgt de voortgang en de curriculumindex en geeft true als behaald.
 * Prestaties belonen aantoonbaar leergedrag (zelfstandig oplossen, fouten oplossen, herhalen),
 * niet het louter openen van pagina's.
 */
const passed = (p) => Object.values(p.exercises).filter((e) => e.status === 'passed');
const count = (p, key) => p.counters[key] || 0;

export const ACHIEVEMENTS = [
  { id: 'hello-world', icon: 'terminal', tier: 'brons', title: 'Hello, World!', description: 'Voer je eerste programma uit.', check: (p) => count(p, 'runs') >= 1 },
  { id: 'first-green', icon: 'checkCircle', tier: 'brons', title: 'Groen licht', description: 'Slaag voor je eerste oefening.', check: (p) => passed(p).length >= 1 },
  { id: 'first-try', icon: 'target', tier: 'brons', title: 'In één keer goed', description: 'Slaag voor een oefening bij je eerste poging.', check: (p) => passed(p).some((e) => e.attemptsAtPass === 1) },
  { id: 'independent-5', icon: 'brain', tier: 'zilver', title: 'Zelfstandig', description: 'Los 5 oefeningen op zonder hints.', check: (p) => passed(p).filter((e) => !e.hintsUsed && !e.solutionViewed).length >= 5 },
  { id: 'independent-25', icon: 'brain', tier: 'goud', title: 'Eigen kracht', description: 'Los 25 oefeningen op zonder hints.', check: (p) => passed(p).filter((e) => !e.hintsUsed && !e.solutionViewed).length >= 25 },
  { id: 'exercises-10', icon: 'code', tier: 'brons', title: 'Op gang', description: 'Slaag voor 10 oefeningen.', check: (p) => passed(p).length >= 10 },
  { id: 'exercises-50', icon: 'code', tier: 'zilver', title: 'Codemachine', description: 'Slaag voor 50 oefeningen.', check: (p) => passed(p).length >= 50 },
  { id: 'exercises-100', icon: 'code', tier: 'goud', title: 'Honderdtal', description: 'Slaag voor 100 oefeningen.', check: (p) => passed(p).length >= 100 },
  { id: 'bug-hunter', icon: 'bug', tier: 'zilver', title: 'Bug Hunter', description: 'Los 10 keer een compileer- of runtimefout op tot je oefening slaagt.', check: (p) => count(p, 'fixedErrors') >= 10 },
  { id: 'persistent', icon: 'heart', tier: 'zilver', title: 'Doorzetter', description: 'Slaag voor een oefening na minstens 5 mislukte pogingen.', check: (p) => passed(p).some((e) => (e.attemptsAtPass || 0) >= 6) },
  { id: 'predictor-10', icon: 'eye', tier: 'brons', title: 'Helderziende', description: 'Voorspel 10 keer correct bij je eerste poging.', check: (p) => count(p, 'predictionsFirstTry') >= 10 },
  { id: 'predictor-50', icon: 'eye', tier: 'goud', title: 'Orakel', description: 'Voorspel 50 keer correct bij je eerste poging.', check: (p) => count(p, 'predictionsFirstTry') >= 50 },
  { id: 'tracer', icon: 'layers', tier: 'brons', title: 'Traceur', description: 'Traceer 5 programma’s stap voor stap.', check: (p) => count(p, 'traces') >= 5 },
  { id: 'debugger', icon: 'bug', tier: 'zilver', title: 'Debugger', description: 'Gebruik een breakpoint en Step Into/Over/Out in de debugger.', check: (p) => count(p, 'breakpointHits') >= 1 && count(p, 'steps') >= 10 },
  { id: 'player', icon: 'keyboard', tier: 'brons', title: 'Speler', description: 'Speel een interactief programma in de terminal.', check: (p) => count(p, 'interactiveRuns') >= 1 },
  { id: 'clean-coder', icon: 'sparkles', tier: 'zilver', title: 'Clean Coder', description: 'Slaag voor 10 oefeningen zonder enige code-reviewopmerking.', check: (p) => passed(p).filter((e) => e.cleanPass).length >= 10 },
  { id: 'archivist', icon: 'database', tier: 'brons', title: 'Archivaris', description: 'Laat een programma een bestand schrijven.', check: (p) => count(p, 'filesWritten') >= 1 },
  { id: 'reviewer-5', icon: 'repeat', tier: 'zilver', title: 'Geheugenkampioen', description: 'Voltooi 5 herhalingen (spaced repetition).', check: (p) => count(p, 'reviewsPassed') >= 5 },
  { id: 'daily-1', icon: 'calendar', tier: 'brons', title: 'Dagtaak', description: 'Voltooi een dagelijkse uitdaging.', check: (p) => count(p, 'dailiesCompleted') >= 1 },
  { id: 'streak-3', icon: 'flame', tier: 'brons', title: 'Op dreef', description: 'Leer 3 dagen op rij.', check: (p) => currentStreak(p) >= 3 },
  { id: 'streak-7', icon: 'flame', tier: 'zilver', title: 'Weekstrijder', description: 'Leer 7 dagen op rij.', check: (p) => currentStreak(p) >= 7 },
  { id: 'streak-30', icon: 'flame', tier: 'goud', title: 'Onstuitbaar', description: 'Leer 30 dagen op rij.', check: (p) => currentStreak(p) >= 30 },
  { id: 'xp-1000', icon: 'zap', tier: 'brons', title: 'Duizendpoot', description: 'Verdien 1000 XP.', check: (p) => p.xp >= 1000 },
  { id: 'xp-5000', icon: 'zap', tier: 'zilver', title: 'Krachtpatser', description: 'Verdien 5000 XP.', check: (p) => p.xp >= 5000 },
  { id: 'xp-15000', icon: 'zap', tier: 'goud', title: 'Legende', description: 'Verdien 15000 XP.', check: (p) => p.xp >= 15000 },
  { id: 'night-owl', icon: 'moon', tier: 'brons', title: 'Nachtuil', description: 'Slaag voor een oefening na 23 uur.', check: (p) => count(p, 'nightPasses') >= 1 },
  { id: 'first-boss', icon: 'sword', tier: 'zilver', title: 'Eerste overwinning', description: 'Versla je eerste boss.', check: (p, c) => bossesDefeated(p, c) >= 1 },
  { id: 'boss-5', icon: 'crown', tier: 'goud', title: 'Bossverslinder', description: 'Versla 5 bosses.', check: (p, c) => bossesDefeated(p, c) >= 5 },
  { id: 'fundamentals', icon: 'graduation', tier: 'zilver', title: 'Fundament gelegd', description: 'Voltooi Level 1: C# Fundamentals.', check: (p, c) => levelDone(p, c, 'L01') },
  { id: 'loops', icon: 'repeat', tier: 'zilver', title: 'Lusmeester', description: 'Voltooi Level 2: Loops & Debugging.', check: (p, c) => levelDone(p, c, 'L02') },
  { id: 'arrays', icon: 'list', tier: 'zilver', title: 'Rij aan rij', description: 'Voltooi Level 4: Arrays.', check: (p, c) => levelDone(p, c, 'L04') },
  { id: 'methods', icon: 'puzzle', tier: 'zilver', title: 'Bouwstenen', description: 'Voltooi Level 6: Methods & Program Structure.', check: (p, c) => levelDone(p, c, 'L06') },
  { id: 'oop', icon: 'box', tier: 'goud', title: 'Objectdenker', description: 'Voltooi Level 10: Object-Oriented Programming.', check: (p, c) => levelDone(p, c, 'L10') },
  { id: 'collections', icon: 'layers', tier: 'goud', title: 'Verzamelaar', description: 'Voltooi Level 12: Collections.', check: (p, c) => levelDone(p, c, 'L12') },
  { id: 'architect', icon: 'shield', tier: 'platina', title: 'Software Architect', description: 'Voltooi Level 14: Interfaces & Professional Software Design.', check: (p, c) => levelDone(p, c, 'L14') },
  { id: 'people-app', icon: 'users', tier: 'platina', title: 'Personeelschef', description: 'Voltooi de volledige personenapplicatie (PA2 t.e.m. PA5).', check: (p, c) => levelDone(p, c, 'PA') },
  { id: 'deep-diver', icon: 'sparkles', tier: 'zilver', title: 'Diepgraver', description: 'Los 3 verdiepingsoefeningen op.', check: (p, c) => passed(p).filter((e) => c?.exercises.get(e.id)?.levelId === 'VD').length >= 3 },
  { id: 'skill-master-5', icon: 'star', tier: 'zilver', title: 'Veelzijdig', description: 'Beheers 5 vaardigheden (beheersing ≥ 80%).', check: (p) => Object.values(p.skills).filter((s) => s.mastery >= 0.8 && (s.passes || 0) >= 2).length >= 5 },
  { id: 'skill-master-20', icon: 'star', tier: 'goud', title: 'Allround programmeur', description: 'Beheers 20 vaardigheden.', check: (p) => Object.values(p.skills).filter((s) => s.mastery >= 0.8 && (s.passes || 0) >= 2).length >= 20 },
];

function levelDone(p, c, id) {
  const level = c?.levelMap.get(id);
  return !!level && isLevelComplete(p, level);
}

function bossesDefeated(p, c) {
  if (!c) return 0;
  return c.orderedLessons.filter((l) => l.kind === 'boss' && isLessonComplete(p, l)).length;
}

export const TIER_COLORS = { brons: '#d08b5b', zilver: '#b8c2d6', goud: '#f5c451', platina: '#9fe7ff' };
