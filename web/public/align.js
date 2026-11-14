// Finds which part of what was typed became one word of the repair. Same steps as the plugin's Align.TypedFor.
// Letters are compared without spaces (the model moves spaces), lower case, in a window around the word, so long
// texts stay fast. Returns [from, to) in the typed text, or null when nothing lines up.
export function typedFor(typed, repaired, start, end) {
  const T = [], R = [];
  for (let i = 0; i < typed.length; i++) if (!/\s/.test(typed[i])) T.push(i);
  for (let i = 0; i < repaired.length; i++) if (!/\s/.test(repaired[i])) R.push(i);
  const a = R.findIndex((i) => i >= start);
  if (a < 0 || R[a] >= end || T.length === 0) return null;
  let b = a;
  while (b + 1 < R.length && R[b + 1] < end) b++;

  const W = 60;
  const ratio = T.length / Math.max(1, R.length);
  const r0 = Math.max(0, a - W), r1 = Math.min(R.length, b + W + 1);
  const t0 = Math.max(0, Math.floor(r0 * ratio) - 20), t1 = Math.min(T.length, Math.ceil(r1 * ratio) + 20);
  const rs = R.slice(r0, r1).map((i) => repaired[i].toLowerCase());
  const ts = T.slice(t0, t1).map((i) => typed[i].toLowerCase());
  const n = rs.length, m = ts.length;

  // Edit distance, free to start and end anywhere in the typed window (it is cut wider than the repair window).
  const d = [];
  for (let i = 0; i <= n; i++) { d.push(new Int32Array(m + 1)); d[i][0] = i; }
  for (let i = 1; i <= n; i++)
    for (let j = 1; j <= m; j++)
      d[i][j] = Math.min(d[i - 1][j - 1] + (rs[i - 1] === ts[j - 1] ? 0 : 1), d[i - 1][j] + 1, d[i][j - 1] + 1);
  let j = 0;
  for (let k = 1; k <= m; k++) if (d[n][k] < d[n][j]) j = k;

  const hit = [];
  for (let i = n; i > 0 && j > 0;) {
    if (d[i][j] === d[i - 1][j - 1] + (rs[i - 1] === ts[j - 1] ? 0 : 1)) {
      if (i - 1 >= a - r0 && i - 1 <= b - r0) hit.push(j - 1);
      i--; j--;
    } else if (d[i][j] === d[i - 1][j] + 1) i--;
    else j--;
  }
  if (hit.length === 0) return null;
  return [T[t0 + Math.min(...hit)], T[t0 + Math.max(...hit)] + 1];
}

// True when b is a with at most two letters dropped, added, changed or swapped (for swear-word suggestions).
export function near(a, b) {
  const n = a.length, m = b.length;
  if (Math.abs(n - m) > 2) return false;
  const d = [];
  for (let i = 0; i <= n; i++) { d.push(new Int32Array(m + 1)); d[i][0] = i; }
  for (let j = 0; j <= m; j++) d[0][j] = j;
  for (let i = 1; i <= n; i++)
    for (let j = 1; j <= m; j++) {
      d[i][j] = Math.min(d[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1), d[i - 1][j] + 1, d[i][j - 1] + 1);
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + 1);
    }
  return d[n][m] <= 2;
}
