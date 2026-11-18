// Centrale import van de UI-bibliotheek (Preact = React-API in 10 kB, zonder build-stap).
// Componenten schrijven JSX-achtige templates met `html\`...\`` (htm).
import { h, render, Fragment, createContext, toChildArray } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback, useContext, useReducer, useLayoutEffect } from 'preact/hooks';
import htm from 'htm';

export const html = htm.bind(h);
export { h, render, Fragment, createContext, toChildArray };
export { useState, useEffect, useRef, useMemo, useCallback, useContext, useReducer, useLayoutEffect };
