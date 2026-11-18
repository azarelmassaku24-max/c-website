import { settings } from './settings.js';

// Lestypes die deel uitmaken van het verplichte leerpad (in volgorde).
export const CORE_KINDS = new Set(['lesson', 'boss', 'project']);

export function isExercisePassed(p, id) {
  return p.exercises[id]?.status === 'passed';
}

/** Een les is voltooid als de meesterschapsopdracht (of alle opdrachten) geslaagd zijn. */
export function isLessonComplete(p, lesson) {
  if (p.lessons[lesson.id]?.completedAt) return true;
  const exercises = lesson.exercises || [];
  if (exercises.length === 0) return false;
  if (lesson.kind === 'lesson') {
    const mastery = exercises.filter((e) => e.role === 'meesterschap');
    const required = mastery.length ? mastery : exercises;
    return required.every((e) => isExercisePassed(p, e.id));
  }
  if (lesson.kind === 'boss' || lesson.kind === 'project') return exercises.every((e) => isExercisePassed(p, e.id));
  return false;
}

export function levelCoreLessons(level) {
  return level.lessons.filter((l) => CORE_KINDS.has(l.kind));
}

export function isLevelComplete(p, level) {
  const core = levelCoreLessons(level);
  return core.length > 0 && core.every((l) => isLessonComplete(p, l));
}

export function isLevelUnlocked(p, curriculum, level) {
  if (settings.get().freeMode) return true;
  return (level.requires || []).every((id) => {
    const req = curriculum.levelMap.get(id);
    return !req || isLevelComplete(p, req);
  });
}

/** status: locked | available | in-progress | completed */
export function lessonStatus(p, curriculum, lesson) {
  if (isLessonComplete(p, lesson)) return 'completed';
  const level = lesson.level;
  if (!isLevelUnlocked(p, curriculum, level)) return 'locked';
  if (!settings.get().freeMode && !lessonRequirementsMet(p, curriculum, lesson)) return 'locked';
  const started = !!p.lessons[lesson.id]?.startedAt;
  if (settings.get().freeMode || !CORE_KINDS.has(lesson.kind)) return started ? 'in-progress' : 'available';
  const core = levelCoreLessons(level);
  const index = core.findIndex((l) => l.id === lesson.id);
  const previous = core.slice(0, index);
  if (previous.every((l) => isLessonComplete(p, l))) return started ? 'in-progress' : 'available';
  return 'locked';
}

function lessonRequirementsMet(p, curriculum, lesson) {
  return (lesson.requires || []).every((id) => {
    const req = curriculum.levelMap.get(id);
    return !req || isLevelComplete(p, req);
  });
}

export function lockReason(p, curriculum, lesson) {
  const level = lesson.level;
  const describe = (ids) => ids.map((id) => curriculum.levelMap.get(id)).filter((l) => l && !isLevelComplete(p, l))
    .map((l) => `Level ${l.number}: ${l.title}`).join(' en ');
  if (!isLevelUnlocked(p, curriculum, level)) return `Voltooi eerst ${describe(level.requires || [])}.`;
  if (!lessonRequirementsMet(p, curriculum, lesson)) return `Voor dit onderdeel heb je eerst ${describe(lesson.requires)} nodig.`;
  const core = levelCoreLessons(level);
  const index = core.findIndex((l) => l.id === lesson.id);
  const prev = core.slice(0, index).find((l) => !isLessonComplete(p, l));
  if (prev) return lesson.kind === 'boss' ? 'Voltooi eerst alle lessen van dit level om de boss uit te dagen.' : `Voltooi eerst “${prev.title}”.`;
  return '';
}

export function levelProgress(p, level) {
  const core = levelCoreLessons(level);
  const done = core.filter((l) => isLessonComplete(p, l)).length;
  return { done, total: core.length, pct: core.length ? Math.round((done / core.length) * 100) : 0 };
}

/** Is de skill (uit de skill tree) ontgrendeld, beheerst, ...? */
export function skillStatus(p, curriculum, skill) {
  const s = p.skills[skill.id];
  const mastery = s?.mastery || 0;
  const level = curriculum.levelMap.get(skill.level);
  if (mastery >= 0.8 && (s?.passes || 0) >= 2) return 'mastered';
  if (s && (s.attempts || 0) > 0) return 'learning';
  const reqOk = (skill.requires || []).every((r) => {
    const rs = p.skills[r];
    const rskill = curriculum.skillMap.get(r);
    return (rs?.passes || 0) > 0 || (rskill && skillLessonsDone(p, curriculum, rskill));
  });
  if (level && isLevelUnlocked(p, curriculum, level) && (reqOk || settings.get().freeMode)) return 'available';
  return 'locked';
}

function skillLessonsDone(p, curriculum, skill) {
  const lessons = curriculum.orderedLessons.filter((l) => l.skills.includes(skill.id));
  return lessons.length > 0 && lessons.every((l) => isLessonComplete(p, l));
}
