import { store } from './store.js';
import { events } from './events.js';
import { levelInfo, hintMultiplier } from './xp.js';
import { today, addDays } from './stats.js';
import { isLessonComplete } from './unlocks.js';
import { ACHIEVEMENTS } from '../achievements/definitions.js';
import { curriculumService } from '../services/curriculumService.js';

/**
 * De leerengine: verwerkt elke leeractie, kent XP toe, werkt de beheersing per vaardigheid bij,
 * registreert veelgemaakte fouten en plant herhalingen (spaced repetition).
 */

const SRS_INTERVALS = [1, 3, 7, 16, 35, 90];

function activity(p) {
  const key = today();
  p.activity[key] ??= { xp: 0, passes: 0, attempts: 0 };
  return p.activity[key];
}

function bump(p, key, n = 1) { p.counters[key] = (p.counters[key] || 0) + n; }

function addHistory(p, entry) {
  p.history.unshift({ at: new Date().toISOString(), ...entry });
  if (p.history.length > 200) p.history.length = 200;
}

function awardXp(p, amount, reason, collected) {
  amount = Math.round(amount);
  if (amount <= 0) return;
  const before = levelInfo(p.xp).level;
  p.xp += amount;
  activity(p).xp += amount;
  collected.push({ type: 'xp', amount, reason });
  const after = levelInfo(p.xp).level;
  if (after > before) collected.push({ type: 'levelUp', level: after });
}

function skill(p, id) {
  p.skills[id] ??= { mastery: 0, attempts: 0, failures: 0, passes: 0, lastPracticed: null };
  return p.skills[id];
}

/** Beheersing bijwerken: sneller stijgen bij zelfstandig succes, licht dalen bij fouten. */
function updateMastery(p, skills, outcome) {
  for (const id of skills || []) {
    const s = skill(p, id);
    s.lastPracticed = new Date().toISOString();
    switch (outcome.kind) {
      case 'attempt':
        s.attempts++;
        if (!outcome.passed) {
          s.failures++;
          s.mastery = Math.max(0, s.mastery - 0.04 * (0.5 + s.mastery));
        }
        break;
      case 'pass':
        s.passes++;
        s.mastery = Math.min(1, s.mastery + 0.22 * outcome.quality * (1 - s.mastery));
        break;
      case 'prediction':
        s.attempts++;
        if (outcome.passed) s.mastery = Math.min(1, s.mastery + 0.07 * (1 - s.mastery));
        else { s.failures++; s.mastery = Math.max(0, s.mastery - 0.03); }
        break;
    }
  }
}

function recordErrors(p, result, skills) {
  const seen = new Set();
  const add = (code, title) => {
    if (!code || seen.has(code)) return;
    seen.add(code);
    const e = (p.errors[code] ??= { count: 0, lastAt: null, skills: {}, title });
    e.count++;
    e.lastAt = new Date().toISOString();
    if (title) e.title = title;
    for (const s of skills || []) e.skills[s] = (e.skills[s] || 0) + 1;
  };
  for (const d of result.diagnostics || []) if (d.severity === 'error') add(d.code, d.message);
  for (const t of result.tests || []) {
    if (t.passed) continue;
    if (t.exception) add(t.exception.type, t.exception.title);
    else add('test:' + t.category, t.category === 'output' ? 'Foutieve output (tekst/opmaak)' : t.status === 'inputExhausted' ? 'Programma vraagt te veel invoer' : t.status === 'timeout' || t.status === 'cpuLimit' ? 'Oneindige loop / tijdslimiet' : 'Logische fout (verkeerd resultaat)');
  }
  for (const r of result.rules || []) if (!r.passed) add('rule:' + r.type, 'Opdrachtvereiste: ' + r.message);
}

function checkAchievements(p, collected) {
  const c = curriculumService.get();
  for (const a of ACHIEVEMENTS) {
    if (p.achievements[a.id]) continue;
    let ok = false;
    try { ok = a.check(p, c); } catch { ok = false; }
    if (ok) {
      p.achievements[a.id] = { unlockedAt: new Date().toISOString() };
      collected.push({ type: 'achievement', achievement: a });
      addHistory(p, { type: 'achievement', id: a.id });
    }
  }
}

function finish(collected) {
  for (const e of collected) events.emit(e);
  return collected;
}

export const engine = {
  startLesson(lessonId) {
    const p = store.get();
    if (p.lessons[lessonId]?.startedAt) return;
    store.update((s) => {
      s.lessons[lessonId] = { step: 0, maxStep: 0, predictions: {}, ...(s.lessons[lessonId] || {}), startedAt: new Date().toISOString() };
    });
  },

  setStep(lessonId, step) {
    store.update((s) => {
      const l = (s.lessons[lessonId] ??= { step: 0, maxStep: 0, predictions: {}, startedAt: new Date().toISOString() });
      l.step = step;
      l.maxStep = Math.max(l.maxStep || 0, step);
    });
  },

  markReviewed(lessonId) {
    store.update((s) => { (s.lessons[lessonId] ??= { predictions: {} }).reviewed = true; });
  },

  recordPrediction(lesson, pid, correct) {
    const collected = [];
    store.update((p) => {
      const l = (p.lessons[lesson.id] ??= { step: 0, maxStep: 0, predictions: {}, startedAt: new Date().toISOString() });
      const pred = (l.predictions[pid] ??= { correct: false, attempts: 0 });
      if (pred.correct) return; // al juist beantwoord
      pred.attempts++;
      activity(p).attempts++;
      if (correct) {
        pred.correct = true;
        pred.answeredAt = new Date().toISOString();
        if (pred.attempts === 1) { awardXp(p, 10, 'Juiste voorspelling', collected); bump(p, 'predictionsFirstTry'); }
        else if (pred.attempts === 2) awardXp(p, 4, 'Voorspelling (2e poging)', collected);
      }
      updateMastery(p, lesson.skills, { kind: 'prediction', passed: correct && pred.attempts === 1 });
      checkAchievements(p, collected);
    });
    return finish(collected);
  },

  recordHint(exerciseId, n) {
    store.update((p) => {
      const e = (p.exercises[exerciseId] ??= newExercise());
      e.hintsUsed = Math.max(e.hintsUsed || 0, n);
    });
  },

  recordSolutionView(exerciseId) {
    store.update((p) => {
      const e = (p.exercises[exerciseId] ??= newExercise());
      if (e.status !== 'passed') e.solutionViewed = true;
      // Wie de oplossing bekeek, krijgt deze oefening binnenkort opnieuw (zonder hulp).
      p.srs[exerciseId] = { interval: 1, due: addDays(today(), 1), reps: 0, lapses: (p.srs[exerciseId]?.lapses || 0) + 1 };
    });
  },

  saveDraft(exerciseId, files) {
    const p = store.get();
    const e = p.exercises[exerciseId];
    if (e && JSON.stringify(e.files) === JSON.stringify(files)) return;
    store.update((s) => {
      const ex = (s.exercises[exerciseId] ??= newExercise());
      ex.files = files;
    });
  },

  saveLab(files) {
    store.update((p) => { p.lab = { files, savedAt: new Date().toISOString() }; });
  },

  recordRun({ interactive = false, files = {} } = {}) {
    const collected = [];
    store.update((p) => {
      bump(p, 'runs');
      if (interactive) bump(p, 'interactiveRuns');
      if (Object.keys(files || {}).length > 0) bump(p, 'filesWritten');
      checkAchievements(p, collected);
    });
    return finish(collected);
  },

  recordTrace() {
    const collected = [];
    store.update((p) => { bump(p, 'traces'); checkAchievements(p, collected); });
    return finish(collected);
  },

  recordDebug({ steps = 0, breakpointHit = false }) {
    const collected = [];
    store.update((p) => {
      bump(p, 'steps', steps);
      if (breakpointHit) bump(p, 'breakpointHits');
      checkAchievements(p, collected);
    });
    return finish(collected);
  },

  /**
   * Verwerkt een beoordeling. exercise = samenvatting uit het curriculum (met .lesson en .skills).
   * Geeft een lijst events terug (xp, achievement, levelUp, lessonComplete, bossDefeated, ...).
   */
  recordGrade(exercise, result, files, { daily = false } = {}) {
    const collected = [];
    store.update((p) => {
      const e = (p.exercises[exercise.id] ??= newExercise());
      const wasPassed = e.status === 'passed';
      const srs = p.srs[exercise.id];
      const isReview = wasPassed && srs && srs.due <= today();
      e.attempts++;
      e.lastAttemptAt = new Date().toISOString();
      e.files = files;
      e.lastSummary = { passed: result.passed, score: result.score, category: result.primaryCategory, quality: result.quality?.length || 0, at: e.lastAttemptAt };
      e.bestScore = Math.max(e.bestScore || 0, result.score || 0);
      const failed = !result.passed;
      if (failed) e.lastFailCategory = result.primaryCategory;
      activity(p).attempts++;

      updateMastery(p, exercise.skills, { kind: 'attempt', passed: !failed });
      if (failed) {
        recordErrors(p, result, exercise.skills);
        if (result.primaryCategory === 'syntax' || result.primaryCategory === 'compile' || result.primaryCategory === 'runtime') e.hadErrors = true;
        if (isReview) { srs.interval = 1; srs.due = addDays(today(), 1); srs.lapses = (srs.lapses || 0) + 1; srs.reps = 0; }
      } else {
        const quality = e.solutionViewed ? 0.25 : hintMultiplier(e.hintsUsed);
        updateMastery(p, exercise.skills, { kind: 'pass', passed: true, quality });
        activity(p).passes++;
        if (e.hadErrors) { bump(p, 'fixedErrors'); e.hadErrors = false; }
        const hour = new Date().getHours();
        if (hour >= 23 || hour < 4) bump(p, 'nightPasses');
        const clean = (result.quality?.length || 0) === 0;

        if (!wasPassed) {
          e.status = 'passed';
          e.firstPassedAt = new Date().toISOString();
          e.attemptsAtPass = e.attempts;
          e.cleanPass = clean;
          let xp = exercise.xp * hintMultiplier(e.hintsUsed);
          if (e.solutionViewed) xp = 0;
          if (e.attempts === 1 && !e.hintsUsed && !e.solutionViewed) xp *= 1.2;
          if (clean && xp > 0) xp *= 1.1;
          e.xpAwarded = Math.round(xp);
          if (xp > 0) awardXp(p, xp, `Oefening “${exercise.title}” geslaagd`, collected);
          else collected.push({ type: 'info', message: 'Geslaagd! Omdat je de oplossing bekeek, verdien je hier geen XP. Deze oefening komt morgen terug als herhaling.' });
          if (!e.solutionViewed) p.srs[exercise.id] = { interval: SRS_INTERVALS[0], due: addDays(today(), SRS_INTERVALS[0]), reps: 0, lapses: 0 };
          addHistory(p, { type: 'pass', id: exercise.id, xp: Math.round(xp) });
        } else if (isReview) {
          srs.reps = (srs.reps || 0) + 1;
          srs.interval = SRS_INTERVALS[Math.min(SRS_INTERVALS.length - 1, srs.reps)];
          srs.due = addDays(today(), srs.interval);
          bump(p, 'reviewsPassed');
          if (e.solutionViewed) { e.solutionViewed = false; }
          awardXp(p, Math.max(10, exercise.xp * 0.3), `Herhaling “${exercise.title}”`, collected);
          addHistory(p, { type: 'review', id: exercise.id });
        } else if (clean && !e.cleanPass) {
          e.cleanPass = true;
          awardXp(p, 10, 'Code verbeterd (clean code)', collected);
        }

        // Dagelijkse uitdaging
        if (p.daily?.date === today() && p.daily.ids.includes(exercise.id) && !p.daily.done.includes(exercise.id)) {
          p.daily.done.push(exercise.id);
          if (p.daily.done.length === p.daily.ids.length && !p.daily.bonusAwarded) {
            p.daily.bonusAwarded = true;
            bump(p, 'dailiesCompleted');
            awardXp(p, 50, 'Dagelijkse uitdaging voltooid', collected);
          }
        }

        // Les / boss / level voltooid?
        const lesson = exercise.lesson;
        if (lesson && !p.lessons[lesson.id]?.completedAt && isLessonComplete(p, lesson)) {
          const l = (p.lessons[lesson.id] ??= { predictions: {}, startedAt: new Date().toISOString() });
          l.completedAt = new Date().toISOString();
          const bonus = lesson.kind === 'boss' ? 150 : lesson.kind === 'project' ? 200 : 30;
          awardXp(p, bonus, lesson.kind === 'boss' ? `Boss “${lesson.title}” verslagen` : `Les “${lesson.title}” voltooid`, collected);
          collected.push({ type: lesson.kind === 'boss' ? 'bossDefeated' : 'lessonComplete', lesson });
          addHistory(p, { type: 'lesson', id: lesson.id });
        }
      }
      checkAchievements(p, collected);
    });
    return finish(collected);
  },

  /** Hercontroleer prestaties (bv. na import). */
  refreshAchievements() {
    const collected = [];
    store.update((p) => checkAchievements(p, collected));
    return finish(collected);
  },
};

function newExercise() {
  return { status: 'new', attempts: 0, hintsUsed: 0, solutionViewed: false, files: null };
}
