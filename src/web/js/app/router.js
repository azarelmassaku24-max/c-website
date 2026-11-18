import { useEffect, useState } from '../lib/preact.js';

// Hash-router: #/les/L01-01 → { path: '/les/L01-01', parts: ['les', 'L01-01'] }
export function currentRoute() {
  const raw = location.hash.replace(/^#/, '') || '/';
  const [path, query = ''] = raw.split('?');
  return { path, parts: path.split('/').filter(Boolean).map(decodeURIComponent), query: new URLSearchParams(query) };
}

export function navigate(path) {
  if (location.hash !== '#' + path) location.hash = path;
}

export function useRoute() {
  const [route, setRoute] = useState(currentRoute);
  useEffect(() => {
    const onChange = () => { setRoute(currentRoute()); window.scrollTo({ top: 0 }); };
    addEventListener('hashchange', onChange);
    return () => removeEventListener('hashchange', onChange);
  }, []);
  return route;
}
