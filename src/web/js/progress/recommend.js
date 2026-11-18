import { store } from './store.js';
import { lessonStatus, isLevelUnlocked } from './unlocks.js';
import { today, weakSkills, dueReviews } from './stats.js';

/**
 * Adaptieve aanbevelingen:
 * - volgende missie in het leerpad;
 * - extra oefeningen voor onderwerpen die moeilijk gaan;
 * - moeilijkheid die meegroeit met de beheersing;
 * - dagelijkse uitdaging = herhaling + zwak onderwerp + nieuwe uitdaging.
 */

export function nextMission(p, c) {
  for (const lesson of c.orderedLessons) {
    if (!['lesson', 'boss', 'project'].includes(lesson.kind)) continue;
    const status = lessonStatus(p, c, lesson);
    if (status === 'available' || status === 'in-progress') {
      const lp = p.lessons[lesson.id];
      return { lesson, status, step: lp?.step || 0 };
    }
  }
  return null;
}

/** Alle oefeningen die de leerling nu mag maken. */
export function availableExercises(p, c) {
  return [...c.exercises.values()].filter((ex) => {
    const lesson = ex.lesson;
    if (!isLevelUnlocked(p, c, lesson.level)) return false;
    if (lesson.kind === 'challenges' || lesson.kind === 'verdieping') return true;
    return lessonStatus(p, c, lesson) !== 'locked';
  });
}

/** Gewenste moeilijkheid (1..5) voor een vaardigheid op basis van beheersing. */
export function targetDifficulty(p, skillId) {
  const m = p.skills[skillId]?.mastery || 0;
  return m < 0.3 ? 1 : m < 0.5 ? 2 : m < 0.7 ? 3 : m < 0.85 ? 4 : 5;
}

/** Extra oefeningen voor één vaardigheid: eerst niet-opgeloste, dicht bij de gewenste moeilijkheid. */
export function practiceFor(p, c, skillId, limit = 4) {
  const target = targetDifficulty(p, skillId);
  return availableExercises(p, c)
    .filter((ex) => ex.skills.includes(skillId) && ex.role !== 'boss')
    .map((ex) => {
      const st = p.exercises[ex.id];
      const solved = st?.status === 'passed';
      const score = (solved ? 10 : 0) + Math.abs(ex.difficulty - target) + (ex.role === 'uitdaging' ? -0.5 : 0) + (st?.solutionViewed ? -3 : 0);
      return { ex, score };
    })
    .sort((a, b) => a.score - b.score)
    .slice(0, limit)
    .map((x) => x.ex);
}

export function recommendations(p, c) {
  const weak = weakSkills(p, c, 4);
  return weak.map((w) => ({ skill: w, exercises: practiceFor(p, c, w.id, 3) })).filter((r) => r.exercises.length > 0);
}

/** Maakt (eens per dag) de dagelijkse uitdaging aan. */
export function ensureDaily(c) {
  const p = store.get();
  if (p.daily?.date === today() && p.daily.ids.length) return p.daily;
  // Enkel leerstof die je al gezien hebt: voltooide lessen + oefenpools van levels waarin je al een les voltooide.
  const levelsStarted = new Set(c.orderedLessons.filter((l) => p.lessons[l.id]?.completedAt).map((l) => l.levelId));
  const pool = availableExercises(p, c).filter((ex) => ex.role !== 'boss' && ex.role !== 'project'
    && (p.lessons[ex.lesson.id]?.completedAt || (ex.lesson.kind === 'challenges' && levelsStarted.has(ex.lesson.levelId))));
  if (!pool.length) return null;
  const chosen = [];
  const pick = (ex) => { if (ex && !chosen.includes(ex.id)) chosen.push(ex.id); };

  // 1. Een herhaling die vervallen is, of een oudere opgeloste oefening.
  const due = dueReviews(p, c);
  if (due.length) pick(due[0].exercise);
  else pick(pool.filter((ex) => p.exercises[ex.id]?.status === 'passed').sort((a, b) => (p.exercises[a.id].firstPassedAt || '').localeCompare(p.exercises[b.id].firstPassedAt || ''))[0]);

  // 2. Een oefening voor het zwakste onderwerp.
  const weak = weakSkills(p, c, 3);
  for (const w of weak) { const ex = practiceFor(p, c, w.id, 2).find((e) => !chosen.includes(e.id)); if (ex) { pick(ex); break; } }

  // 3. Een nieuwe uitdaging uit de oefenpool van een ontgrendeld level.
  const fresh = pool.filter((ex) => ex.role === 'uitdaging' && p.exercises[ex.id]?.status !== 'passed' && !chosen.includes(ex.id));
  const seed = [...today()].reduce((s, ch) => s + ch.charCodeAt(0), 0);
  if (fresh.length) pick(fresh[seed % fresh.length]);

  // Aanvullen tot 3 met niet-opgeloste oefeningen.
  const rest = pool.filter((ex) => p.exercises[ex.id]?.status !== 'passed' && !chosen.includes(ex.id));
  while (chosen.length < 3 && rest.length) pick(rest.splice(seed % rest.length, 1)[0]);

  store.update((s) => { s.daily = { date: today(), ids: chosen, done: [], bonusAwarded: false }; });
  return store.get().daily;
}
