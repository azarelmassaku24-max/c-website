import { useEffect, useState } from '../lib/preact.js';
import { store } from '../progress/store.js';
import { settings } from '../progress/settings.js';

/** Herrendert de component wanneer de voortgang verandert. */
export function useProgress() {
  const [, setTick] = useState(0);
  useEffect(() => store.subscribe(() => setTick((t) => t + 1)), []);
  return store.get();
}

export function useSettings() {
  const [s, setS] = useState(settings.get());
  useEffect(() => settings.subscribe(setS), []);
  return s;
}

export function useMediaQuery(query) {
  const [match, setMatch] = useState(() => matchMedia(query).matches);
  useEffect(() => {
    const mq = matchMedia(query);
    const on = () => setMatch(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [query]);
  return match;
}
