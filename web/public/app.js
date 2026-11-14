import { typedFor, near } from './align.js';
import { SWEAR_WORDS } from './swearwords.js';

const $ = (id) => document.getElementById(id);
const { t, toggle } = window.I18N;
const worker = new Worker('worker.js', { type: 'module' });
const input = $('input'), go = $('go'), status = $('status');
const keepWords = $('keep');
let nextId = 0, currentId = 0, isReady = false, typedText = '';

// The never-change list stays in this browser and is only ever handed to the local worker.
try { keepWords.value = localStorage.getItem('typemend_keep') || ''; } catch (_) {}
keepWords.addEventListener('input', () => {
  try { localStorage.setItem('typemend_keep', keepWords.value); } catch (_) {}
});

const setStatus = (key) => { status.dataset.i18n = key; status.textContent = t(key); };

function showRepair(text, unsure) {
  const box = $('repairedText');
  box.textContent = '';
  const flagged = new Set(unsure);
  text.split(/( +|\n)/).forEach((part) => {
    if (flagged.has(part)) {
      const mark = document.createElement('mark');
      mark.textContent = part;
      mark.tabIndex = 0;
      mark.setAttribute('role', 'button');
      mark.setAttribute('aria-haspopup', 'menu');
      mark.title = t('fixHint');
      box.append(mark);
    } else {
      box.append(part);
    }
  });
  $('typed').textContent = typedText;
  $('guessNote').hidden = unsure.length === 0;
  $('copied').textContent = '';
  $('result').hidden = false;
}

function repair() {
  const text = input.value;
  if (!isReady || !text.trim()) return;
  typedText = text;
  closeFix();
  currentId = ++nextId;
  go.disabled = true;
  setStatus('working');
  worker.postMessage({ id: currentId, text, keep: keepWords.value.split(/[\s,]+/).filter(Boolean) });
}

// ---- click-to-fix: a guessed word opens a small menu with other suggestions, the typed letters, or keep ----
let candidateId = 0;
const waiting = new Map();
const askCandidates = (text) => new Promise((resolve) => {
  const id = ++candidateId;
  waiting.set(id, resolve);
  worker.postMessage({ type: 'candidates', id, text });
});
const menu = $('fixMenu');
// Suggestions are offered only when made of real words (the 25,558-word list the model was checked with), words
// the person typed, or their never-change words. Swear words only when the typed letters are close to one.
const SWEARS = new Set(SWEAR_WORDS.map((w) => w.toLowerCase()));
let knownWords = null;
const loadWords = () => knownWords || (knownWords = fetch('model/words.txt').then((r) => r.text()).then((t) => new Set(t.split('\n'))).catch(() => new Set()));
const tokens = (text) => text.toLowerCase().match(/[a-z']+/g) || [];
function acceptable(candidate, typedPart, words) {
  const typed = new Set(tokens(typedText));
  const keep = new Set(tokens(keepWords.value));
  for (const w of tokens(candidate)) {
    if (SWEARS.has(w)) { if (!tokens(typedPart || '').some((x) => near(x, w))) return false; continue; }
    if (!words.has(w) && !typed.has(w) && !keep.has(w)) return false;
  }
  return true;
}
let openMark = null;

function closeFix() {
  menu.hidden = true;
  menu.textContent = '';
  if (openMark) openMark.setAttribute('aria-expanded', 'false');
  openMark = null;
}

function replaceMark(mark, text) {
  const box = $('repairedText');
  mark.replaceWith(document.createTextNode(text));
  box.normalize();
  $('guessNote').hidden = !box.querySelector('mark');
  closeFix();
  box.focus();
}

function option(label, onPick, extraClass) {
  const b = document.createElement('button');
  b.type = 'button';
  b.setAttribute('role', 'menuitem');
  if (extraClass) b.className = extraClass;
  b.textContent = label;
  b.addEventListener('click', onPick);
  return b;
}

function heading(key) {
  const h = document.createElement('div');
  h.className = 'fix-head';
  h.textContent = t(key);
  return h;
}

function openFix(mark) {
  closeFix();
  openMark = mark;
  mark.setAttribute('aria-expanded', 'true');
  const box = $('repairedText');
  const before = document.createRange();
  before.setStart(box, 0);
  before.setEndBefore(mark);
  const start = before.toString().length;
  const word = mark.textContent;
  const span = typedFor(typedText, box.textContent, start, start + word.length);
  const typedPart = span ? typedText.slice(span[0], span[1]) : null;

  menu.append(heading('fixSuggestions'));
  const list = document.createElement('div');
  list.className = 'fix-list';
  list.textContent = t('fixLoading');
  menu.append(list);
  if (typedPart && typedPart !== word) {
    menu.append(heading('fixTyped'));
    menu.append(option(typedPart, () => replaceMark(mark, typedPart), 'typed'));
  }
  menu.append(option(`${t('fixKeep')} "${word}"`, () => replaceMark(mark, word), 'keep-word'));

  const rect = mark.getBoundingClientRect();
  menu.hidden = false;
  const left = Math.min(rect.left + window.scrollX, window.scrollX + document.documentElement.clientWidth - menu.offsetWidth - 16);
  menu.style.left = `${Math.max(window.scrollX + 16, left)}px`;
  menu.style.top = `${rect.bottom + window.scrollY + 6}px`;
  menu.querySelector('button')?.focus();

  // The model sees the typed words around this spot too, then only the part of each alternative that lines up
  // with the typed letters is offered.
  let ws = 0, we = typedText.length, s0 = 0, e0 = 0;
  if (span) {
    ws = Math.max(0, span[0] - 25); we = Math.min(typedText.length, span[1] + 25);
    while (ws > 0 && !/\s/.test(typedText[ws - 1])) ws--;
    while (we < typedText.length && !/\s/.test(typedText[we])) we++;
    s0 = span[0] - ws; e0 = span[1] - ws;
  }
  const windowText = span ? typedText.slice(ws, we) : word;
  Promise.all([askCandidates(windowText), loadWords()]).then(([found, words]) => {
    if (openMark !== mark) return;
    const picks = [];
    for (const f of found) {
      let c = f;
      if (span) {
        const r = typedFor(f, windowText, s0, e0);
        if (!r) continue;
        let [a0, a1] = r;
        while (a0 > 0 && !/\s/.test(f[a0 - 1])) a0--;
        while (a1 < f.length && !/\s/.test(f[a1])) a1++;
        c = f.slice(a0, a1);
      }
      const parts = (x) => x.trim().split(/\s+/).length;
      if (parts(c) !== parts(word)) continue;   // one word gets one-word suggestions
      if (c && c !== word && c !== typedPart && !picks.includes(c) && acceptable(c, typedPart, words)) picks.push(c);
      if (picks.length === 3) break;
    }
    list.textContent = picks.length ? '' : t('fixNone');
    for (const c of picks) list.append(option(c, () => replaceMark(mark, c)));
    if (!menu.contains(document.activeElement)) menu.querySelector('button')?.focus();
  });
}

$('repairedText').addEventListener('click', (e) => {
  const mark = e.target.closest('mark');
  if (mark) { e.preventDefault(); openFix(mark); }
});
$('repairedText').addEventListener('keydown', (e) => {
  const mark = e.target.closest && e.target.closest('mark');
  if (mark && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); openFix(mark); }
});
document.addEventListener('mousedown', (e) => {
  if (!menu.hidden && !menu.contains(e.target) && !e.target.closest('mark')) closeFix();
});
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && !menu.hidden) { const m = openMark; closeFix(); m?.focus(); }
});
window.addEventListener('resize', closeFix);

worker.onmessage = ({ data }) => {
  if (data.type === 'candidates') {
    waiting.get(data.id)?.(data.list);
    waiting.delete(data.id);
    return;
  }
  if (data.type === 'ready') {
    isReady = true;
    go.disabled = false;
    setStatus('readyHint');
    status.title = data.backend;
    window.typemendBackend = data.backend;
  } else if (data.type === 'failed') {
    setStatus('failed');
    console.error(data.error);
  } else if (data.id === currentId) {
    go.disabled = false;
    if (data.type === 'done') {
      setStatus('readyHint');
      showRepair(data.text, data.unsure);
      window.typemendLast = data;
    } else {
      setStatus('repairFailed');
      console.error(data.error);
    }
  }
};

async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    $('copied').textContent = t('copied');
  } catch {
    $('copied').textContent = t('copyFailed');
  }
}
const repairedNow = () => $('repairedText').innerText.trim();

go.addEventListener('click', repair);
input.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); repair(); }
});
$('copy').addEventListener('click', () => copy(repairedNow()));
$('copyBoth').addEventListener('click', () => copy(`${repairedNow()}\n(${t('typedPrefix')}: ${typedText})`));
$('lang').addEventListener('click', toggle);
$('theme').addEventListener('click', () => window.toggleTheme());
