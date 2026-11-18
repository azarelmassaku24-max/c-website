// Instellingen die ook vóór het laden van de voortgang nodig zijn (thema, motion) leven in localStorage.
const KEY = 'csq.settings';
const DEFAULTS = {
  theme: 'dark',          // dark | light | system
  culture: 'nl-BE',       // getalnotatie in de sandbox
  editorFontSize: 14,
  minimap: false,
  freeMode: false,        // alle modules ontgrendeld
  reducedMotion: false,
  sound: false,
  liveDiagnostics: true,
  intellisense: true,
};

let current = load();
const listeners = new Set();

function load() {
  try { return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(KEY) || '{}') }; }
  catch { return { ...DEFAULTS }; }
}

export const settings = {
  get: () => current,
  set(patch) {
    current = { ...current, ...patch };
    try { localStorage.setItem(KEY, JSON.stringify(current)); } catch { /* opslag geblokkeerd */ }
    applyTheme();
    listeners.forEach((l) => l(current));
  },
  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
  defaults: DEFAULTS,
};

export function applyTheme() {
  let theme = current.theme;
  if (theme === 'system') theme = matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  document.documentElement.dataset.theme = theme;
  if (current.reducedMotion) document.documentElement.dataset.motion = 'reduced';
  else delete document.documentElement.dataset.motion;
}

export function effectiveTheme() {
  const t = current.theme;
  if (t === 'system') return matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  return t;
}
