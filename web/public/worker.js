// Runs the repair model off the page's main thread. Same steps as the plugin's Repairer.cs.
const BOS = 1, EOS = 2, OFFSET = 29, MAX_PIECE = 300, UNSURE_BELOW = 0.9;
let ort, enc, dec, backend;

// WebAssembly on purpose: measured against WebGPU, it was 2-6x faster on this small model and a 4x smaller download.
async function load() {
  ort = await import('./vendor/ort.wasm.min.mjs');
  ort.env.wasm.wasmPaths = new URL('./vendor/', self.location.href).href;
  ort.env.wasm.numThreads = self.crossOriginIsolated ? Math.min(4, navigator.hardwareConcurrency || 1) : 1;
  const options = { executionProviders: ['wasm'] };
  enc = await ort.InferenceSession.create('./model/enc.q.onnx', options);
  dec = await ort.InferenceSession.create('./model/dec.q.onnx', options);
  backend = `wasm x${ort.env.wasm.numThreads}`;
}

function pieces(text) {
  const out = [];
  let rest = text.trim();
  while (rest.length > MAX_PIECE) {
    let cut = rest.lastIndexOf(' ', MAX_PIECE);
    if (cut < 20) cut = MAX_PIECE;
    out.push(rest.slice(0, cut));
    rest = rest.slice(cut).trimStart();
  }
  if (rest) out.push(rest);
  return out;
}

async function decode(piece) {
  const x = BigInt64Array.from([...piece], (c) => BigInt(c.charCodeAt(0) - OFFSET));
  const { m } = await enc.run({ x: new ort.Tensor('int64', x, [1, x.length]) });
  const ids = [BigInt(BOS)], conf = [];
  let text = '';
  const limit = piece.length + 6 + Math.floor(piece.length / 4);
  let finished = false;
  for (let step = 0; step < limit; step++) {
    const { o } = await dec.run({ y: new ort.Tensor('int64', BigInt64Array.from(ids), [1, ids.length]), m });
    const s = o.data;
    let best = 0;
    for (let i = 1; i < s.length; i++) if (s[i] > s[best]) best = i;
    if (best === EOS) { finished = true; break; }
    let sum = 0;
    for (let i = 0; i < s.length; i++) sum += Math.exp(s[i] - s[best]);
    ids.push(BigInt(best));
    conf.push(1 / sum);
    text += String.fromCharCode(best + OFFSET);
  }
  // A model that never stops is repeating itself: keep the writer's own text, marked as unsure.
  if (!finished) return { text: piece, conf: new Array(piece.length).fill(0) };
  return { text, conf };
}

function unsureWords({ text, conf }, into) {
  let start = 0;
  for (let i = 0; i <= text.length; i++) {
    if (i < text.length && text[i] !== ' ') continue;
    if (i > start && Math.min(...conf.slice(start, i)) < UNSURE_BELOW) into.push(text.slice(start, i));
    start = i + 1;
  }
}

async function repairRun(run, unsure) {
  const done = [];
  for (const piece of pieces(run)) {
    const d = await decode(piece);
    done.push(d.text);
    unsureWords(d, unsure);
  }
  return done.join(' ');
}

// Words stretched on purpose have four or more of one letter in a row. Same steps as the plugin's RunKeepingStretches.
const STRETCH = /([a-z])\1{3,}/gi, SHORTENED = /([a-z])\1{2,}/gi;
const collapse = (t) => t.replace(STRETCH, (m) => m[0].repeat(3));
const STRETCHED_WORD = new RegExp(`(\\S*(?:${[...'abcdefghijklmnopqrstuvwxyz'].map((c) => `${c}{4}`).join('|')})\\S*)`, 'i');
const KEPT = /(\S*[^\x20-\x7e]\S*|\n+|<[^<>\s]+>)/;

// Repairs the parts between kept ones; kept parts sit at odd positions after split().
async function around(text, kept, inner, unsure) {
  const parts = text.split(kept);
  let out = '';
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    if (i % 2 === 1 || !/[a-z]/i.test(part)) { out += part; continue; }
    if (part.startsWith(' ')) out += ' ';
    out += await inner(part, unsure);
    if (part.endsWith(' ')) out += ' ';
  }
  return out;
}

// The whole sentence is repaired with shortened stretches; if those cannot be matched back, the stretched words stay as typed.
async function repairKeepingStretches(run, unsure) {
  const stretches = run.match(STRETCH);
  if (!stretches) return repairRun(run, unsure);
  const marked = [];
  const collapsed = collapse(run);
  const first = await repairRun(collapsed, marked);
  const found = [...first.matchAll(SHORTENED)];
  const typed = collapsed.replaceAll(' ', '').toLowerCase();
  // The model may only move a letter across a space, not change the letters of a stretched word.
  const sameLetters = (m) => {
    let from = m.index, to = from + m[0].length;
    while (from > 0 && /[a-z]/i.test(first[from - 1])) from--;
    while (to < first.length && /[a-z]/i.test(first[to])) to++;
    return typed.includes(first.slice(from, to).toLowerCase());
  };
  if (found.length === stretches.length && found.every((m, i) => m[0][0].toLowerCase() === stretches[i][0].toLowerCase() && sameLetters(m))) {
    let next = 0;
    const restored = first.replace(SHORTENED, () => stretches[next++]);
    for (const word of restored.split(' ')) if (marked.includes(word) || marked.includes(collapse(word))) unsure.push(word);
    return restored;
  }
  return around(run, STRETCHED_WORD, repairRun, unsure);
}

let keepPattern = null;
function setKeepWords(words) {
  const seen = new Set();
  const list = (words || []).filter((w) => !seen.has(w.toLowerCase()) && seen.add(w.toLowerCase())).map((w) => w.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&'));
  keepPattern = list.length ? new RegExp(`(?<![\\p{L}\\p{N}'])(${list.join('|')})(?![\\p{L}\\p{N}'])`, 'giu') : null;
}

// The whole sentence is repaired first, for its context; if the model altered a never-change word, the text is repaired around those words.
async function repairKeepingNames(run, unsure) {
  const typed = keepPattern && run.match(keepPattern);
  if (!typed) return repairKeepingStretches(run, unsure);
  const marked = [];
  const first = await repairKeepingStretches(run, marked);
  const found = first.match(keepPattern) || [];
  if (found.length === typed.length && found.every((w, i) => w === typed[i])) {
    unsure.push(...marked.filter((w) => !typed.includes(w)));
    return first;
  }
  return around(run, keepPattern, repairKeepingStretches, unsure);
}

// Anything the model does not know (umlauts, emoji, line breaks, <placeholders>) is passed through untouched.
async function repair(text, keep) {
  setKeepWords(keep);
  const unsure = [];
  return { text: await around(text, KEPT, repairKeepingNames, unsure), unsure };
}

const ready = load().then(
  () => postMessage({ type: 'ready', backend }),
  (e) => postMessage({ type: 'failed', error: String(e) }),
);

self.onmessage = async ({ data }) => {
  await ready;
  const start = performance.now();
  try {
    postMessage({ type: 'done', id: data.id, ...(await repair(data.text, data.keep)), ms: Math.round(performance.now() - start) });
  } catch (e) {
    postMessage({ type: 'error', id: data.id, error: String(e) });
  }
};
