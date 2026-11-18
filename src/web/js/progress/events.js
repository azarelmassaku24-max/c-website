// Eenvoudige event bus voor beloningen en meldingen (XP, prestaties, level up, ...).
const listeners = new Set();

export const events = {
  emit(event) { listeners.forEach((l) => { try { l(event); } catch (e) { console.error(e); } }); },
  subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
};
