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

// Anything the model does not know (umlauts, emoji, line breaks, <placeholders>) is passed through untouched.
async function repair(text) {
  const unsure = [];
  const parts = text.split(/(\S*[^\x20-\x7e]\S*|\n+|<[^<>\s]+>)/);
  let out = '';
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    if (i % 2 === 1 || !/[a-z]/i.test(part)) { out += part; continue; }
    if (part.startsWith(' ')) out += ' ';
    out += await repairRun(part, unsure);
    if (part.endsWith(' ')) out += ' ';
  }
  return { text: out, unsure };
}

const ready = load().then(
  () => postMessage({ type: 'ready', backend }),
  (e) => postMessage({ type: 'failed', error: String(e) }),
);

self.onmessage = async ({ data }) => {
  await ready;
  const start = performance.now();
  try {
    postMessage({ type: 'done', id: data.id, ...(await repair(data.text)), ms: Math.round(performance.now() - start) });
  } catch (e) {
    postMessage({ type: 'error', id: data.id, error: String(e) });
  }
};
