import { api } from './api.js';

/**
 * Toegang tot de leerinhoud (laag 'curriculum'). De volledige structuur wordt één keer geladen,
 * lessen en oefeningen worden op aanvraag opgehaald en gecachet.
 */
let curriculum = null;
const lessonCache = new Map();
const exerciseCache = new Map();

export const curriculumService = {
  async load(force = false) {
    if (curriculum && !force) return curriculum;
    const data = await api.get('/api/curriculum');
    curriculum = index(data);
    return curriculum;
  },
  get: () => curriculum,
  async lesson(id) {
    if (!lessonCache.has(id)) lessonCache.set(id, api.get(`/api/lessons/${encodeURIComponent(id)}`).catch((e) => { lessonCache.delete(id); throw e; }));
    return lessonCache.get(id);
  },
  async exercise(id) {
    if (!exerciseCache.has(id)) exerciseCache.set(id, api.get(`/api/exercises/${encodeURIComponent(id)}`).catch((e) => { exerciseCache.delete(id); throw e; }));
    return exerciseCache.get(id);
  },
  hint: (id, n) => api.get(`/api/exercises/${encodeURIComponent(id)}/hints/${n}`),
  solution: (id) => api.get(`/api/exercises/${encodeURIComponent(id)}/solution`),
  checkPrediction: (lessonId, pid, answer) => api.post(`/api/lessons/${encodeURIComponent(lessonId)}/predictions/${encodeURIComponent(pid)}/check`, answer),
  knowledge: () => api.get('/api/knowledge'),
  system: () => api.get('/api/system'),
};

/** Bouwt opzoektabellen: lessen, oefeningen, skills per id. */
function index(data) {
  const lessons = new Map();
  const exercises = new Map();
  const levels = new Map();
  const orderedLessons = [];
  for (const level of data.levels) {
    levels.set(level.id, level);
    for (const lesson of level.lessons) {
      lesson.level = level;
      lessons.set(lesson.id, lesson);
      orderedLessons.push(lesson);
      for (const ex of lesson.exercises) {
        ex.lesson = lesson;
        exercises.set(ex.id, ex);
      }
    }
  }
  const skills = new Map(data.skills.map((s) => [s.id, s]));
  return { ...data, lessons, exercises, levelMap: levels, orderedLessons, skillMap: skills };
}
