import { html, useEffect, useRef, useState } from '../../lib/preact.js';
import { colorize } from '../code-editor/monaco.js';

/** Lichte, alleen-lezen codeweergave met regelnummers, breakpoints en een gemarkeerde huidige regel. */
export function CodeView({ code, currentLine, breakpoints, onToggleBreakpoint, lineNote }) {
  const [lines, setLines] = useState(() => code.split('\n').map(escape));
  const ref = useRef(null);

  useEffect(() => {
    let alive = true;
    setLines(code.split('\n').map(escape));
    colorize(code).then((h) => {
      if (!alive) return;
      const parts = h.split(/<br\s*\/?>/);
      setLines(parts.length >= code.split('\n').length ? parts : code.split('\n').map(escape));
    }).catch(() => {});
    return () => { alive = false; };
  }, [code]);

  useEffect(() => {
    const el = ref.current?.querySelector('.cv-line.current');
    el?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [currentLine]);

  return html`<div class="codeview" ref=${ref}>
    ${lines.map((l, i) => {
      const n = i + 1;
      const bp = breakpoints?.has(n);
      return html`<div class=${`cv-line ${n === currentLine ? 'current' : ''} ${bp ? 'bp' : ''}`} key=${n}>
        <button class="cv-gutter" onClick=${() => onToggleBreakpoint?.(n)} title="Breakpoint aan/uit" aria-label=${`Breakpoint regel ${n}`}>
          <span class="cv-dot"></span><span class="cv-num">${n}</span>
        </button>
        <span class="cv-code" dangerouslySetInnerHTML=${{ __html: l || ' ' }}></span>
        ${n === currentLine && lineNote ? html`<span class="cv-note">${lineNote}</span>` : null}
      </div>`;
    })}
  </div>`;
}

function escape(s) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
