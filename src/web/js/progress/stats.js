// Afgeleide statistieken over de voortgang.

export const today = () => dateKey(new Date());

export function dateKey(d) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function addDays(key, n) {
  const [y, m, d] = key.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  date.setDate(date.getDate() + n);
  return dateKey(date);
}

/** Aantal opeenvolgende dagen (t.e.m. vandaag of gisteren) met leeractiviteit. */
export function currentStreak(p) {
  const active = (k) => {
    const a = p.activity?.[k];
    return a && (a.xp > 0 || a.passes > 0 || a.attempts > 0);
  };
  let key = today();
  if (!active(key)) key = addDays(key, -1);
  let streak = 0;
  while (active(key)) { streak++; key = addDays(key, -1); }
  return streak;
}

export function summary(p) {
  const exercises = Object.values(p.exercises);
  const passed = exercises.filter((e) => e.status === 'passed');
  const attempts = exercises.reduce((s, e) => s + (e.attempts || 0), 0);
  const predictions = Object.values(p.lessons).flatMap((l) => Object.values(l.predictions || {}));
  const firstTry = predictions.filter((x) => x.correct && x.attempts === 1).length;
  return {
    passed: passed.length,
    attempted: exercises.filter((e) => (e.attempts || 0) > 0).length,
    attempts,
    avgAttempts: passed.length ? (passed.reduce((s, e) => s + (e.attemptsAtPass || e.attempts || 1), 0) / passed.length) : 0,
    hints: exercises.reduce((s, e) => s + (e.hintsUsed || 0), 0),
    solutions: exercises.filter((e) => e.solutionViewed).length,
    predictions: predictions.length,
    predictionAccuracy: predictions.length ? firstTry / predictions.length : 0,
    lessonsCompleted: Object.values(p.lessons).filter((l) => l.completedAt).length,
    streak: currentStreak(p),
    cleanPasses: passed.filter((e) => e.cleanPass).length,
  };
}

/** Zwakke onderwerpen: lage beheersing na meerdere pogingen of veel mislukkingen. */
export function weakSkills(p, curriculum, limit = 5) {
  return Object.entries(p.skills)
    .map(([id, s]) => ({ id, ...s, skill: curriculum?.skillMap.get(id), failRate: s.attempts ? (s.failures || 0) / s.attempts : 0 }))
    .filter((s) => (s.attempts || 0) >= 2 && (s.mastery < 0.55 || s.failRate > 0.5))
    .sort((a, b) => a.mastery - b.mastery || b.failRate - a.failRate)
    .slice(0, limit);
}

export function topErrors(p, limit = 8) {
  return Object.entries(p.errors)
    .map(([code, e]) => ({ code, ...e }))
    .sort((a, b) => b.count - a.count)
    .slice(0, limit);
}

export function dueReviews(p, curriculum) {
  const t = today();
  return Object.entries(p.srs)
    .filter(([id, s]) => s.due <= t && curriculum?.exercises.has(id))
    .sort((a, b) => a[1].due.localeCompare(b[1].due))
    .map(([id, s]) => ({ id, ...s, exercise: curriculum.exercises.get(id) }));
}
