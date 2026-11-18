import { api } from '../services/api.js';

/**
 * Voortgangsopslag.
 * - Primair: de server (data/progress/<profiel>.json) → overleeft het wissen van de browsercache.
 * - Secundair: localStorage als snelle cache en als fallback wanneer de server even weg is.
 * Het datamodel heeft een versienummer zodat later accounts/synchronisatie toegevoegd kunnen worden.
 */
export const PROGRESS_VERSION = 1;
const PROFILE = 'default';
const LOCAL_KEY = `csq.progress.${PROFILE}`;

export function emptyProgress() {
  const now = new Date().toISOString();
  return {
    version: PROGRESS_VERSION,
    profile: { id: PROFILE, name: 'Leerling', createdAt: now },
    updatedAt: now,
    xp: 0,
    lessons: {},       // id -> { step, maxStep, startedAt, completedAt, predictions: {pid: {correct, attempts}}, reviewed }
    exercises: {},     // id -> { attempts, passes, status, firstPassedAt, lastAttemptAt, bestScore, hintsUsed, solutionViewed, xpAwarded, files, failsBeforePass, lastSummary, cleanPass }
    skills: {},        // skill -> { mastery, attempts, failures, passes, lastPracticed }
    errors: {},        // code -> { count, lastAt, skills: {skill: n}, title }
    srs: {},           // exerciseId -> { interval, due, reps, lapses }
    achievements: {},  // id -> { unlockedAt }
    activity: {},      // yyyy-mm-dd -> { xp, passes, attempts }
    daily: null,       // { date, ids: [], done: [] , bonusAwarded }
    counters: {},      // runs, traces, breakpoints, interactiveRuns, fixedCompileErrors, ...
    lab: null,         // playground-bestanden
    history: [],       // laatste gebeurtenissen
  };
}

let state = null;
const listeners = new Set();
let saveTimer = null;
let serverAvailable = true;
let lastSaveError = null;

function migrate(data) {
  const base = emptyProgress();
  if (!data || typeof data !== 'object') return base;
  // Ontbrekende velden aanvullen (voorwaartse compatibiliteit).
  const merged = { ...base, ...data, version: PROGRESS_VERSION };
  for (const key of ['lessons', 'exercises', 'skills', 'errors', 'srs', 'achievements', 'activity', 'counters']) {
    if (typeof merged[key] !== 'object' || merged[key] === null) merged[key] = {};
  }
  if (!Array.isArray(merged.history)) merged.history = [];
  return merged;
}

export const store = {
  async load() {
    let server = null, local = null;
    try { local = JSON.parse(localStorage.getItem(LOCAL_KEY) || 'null'); } catch { local = null; }
    try {
      server = await api.get(`/api/progress/${PROFILE}`, { timeout: 8000 });
    } catch (e) {
      if (e.status !== 404) serverAvailable = false;
    }
    // Neem de meest recente versie (bv. als de server even onbereikbaar was).
    let chosen = server;
    if (local && (!server || (local.updatedAt || '') > (server.updatedAt || ''))) chosen = local;
    state = migrate(chosen);
    if (chosen === local && local && serverAvailable) this.save(true);
    return state;
  },

  get: () => state,

  /** Wijzig de voortgang via een functie: update(s => { s.xp += 10 }). */
  update(fn) {
    fn(state);
    state.updatedAt = new Date().toISOString();
    listeners.forEach((l) => l(state));
    this.save();
  },

  replace(data) {
    state = migrate(data);
    state.updatedAt = new Date().toISOString();
    listeners.forEach((l) => l(state));
    this.save(true);
  },

  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },

  save(immediate = false) {
    try { localStorage.setItem(LOCAL_KEY, JSON.stringify(state)); } catch { /* quota of geblokkeerd */ }
    clearTimeout(saveTimer);
    const run = async () => {
      try {
        await api.put(`/api/progress/${PROFILE}`, state, { timeout: 10000 });
        serverAvailable = true;
        lastSaveError = null;
      } catch (e) {
        serverAvailable = false;
        lastSaveError = e.message;
      }
    };
    if (immediate) run(); else saveTimer = setTimeout(run, 800);
  },

  async reset() {
    try { await api.del(`/api/progress/${PROFILE}`); } catch { /* server offline */ }
    try { localStorage.removeItem(LOCAL_KEY); } catch { /* */ }
    state = emptyProgress();
    listeners.forEach((l) => l(state));
    this.save(true);
  },

  status: () => ({ serverAvailable, lastSaveError }),
};

// Bij het sluiten van het tabblad nog snel bewaren.
addEventListener('beforeunload', () => {
  if (!state) return;
  try {
    localStorage.setItem(LOCAL_KEY, JSON.stringify(state));
    navigator.sendBeacon?.(`/api/progress/${PROFILE}`, new Blob([JSON.stringify(state)], { type: 'application/json' }));
  } catch { /* */ }
});
