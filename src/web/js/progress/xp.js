// XP- en levelsysteem. Totale XP om level L te bereiken: 60 * L^2 (level 1 = 60 XP, level 10 = 6000 XP).

export const xpForLevel = (level) => 60 * level * level;

export function levelInfo(xp) {
  let level = 0;
  while (xp >= xpForLevel(level + 1)) level++;
  const current = xpForLevel(level);
  const next = xpForLevel(level + 1);
  return { level, xp, current, next, inLevel: xp - current, needed: next - current, pct: Math.min(100, ((xp - current) / (next - current)) * 100) };
}

const DEFAULT_RANKS = [
  { minLevel: 0, title: 'Rekruut' },
  { minLevel: 2, title: 'Code Cadet' },
  { minLevel: 4, title: 'Loop Ranger' },
  { minLevel: 6, title: 'Array Adept' },
  { minLevel: 8, title: 'Method Smith' },
  { minLevel: 10, title: 'Object Architect' },
  { minLevel: 13, title: 'Collection Master' },
  { minLevel: 16, title: 'Interface Engineer' },
  { minLevel: 20, title: 'C# Professional' },
];

export function rankFor(level, ranks) {
  const list = ranks?.length ? ranks : DEFAULT_RANKS;
  let title = list[0].title;
  for (const r of list) if (level >= r.minLevel) title = r.title;
  return title;
}

/** XP-vermenigvuldiger op basis van gebruikte hulp. */
export function hintMultiplier(hintsUsed) {
  return [1, 0.85, 0.7, 0.5][Math.min(3, hintsUsed || 0)];
}
