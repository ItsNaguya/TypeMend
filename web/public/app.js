const $ = (id) => document.getElementById(id);
const { t, toggle } = window.I18N;
const worker = new Worker('worker.js', { type: 'module' });
const input = $('input'), go = $('go'), status = $('status');
let nextId = 0, currentId = 0, isReady = false, typedText = '';

const setStatus = (key) => { status.dataset.i18n = key; status.textContent = t(key); };

function showRepair(text, unsure) {
  const box = $('repairedText');
  box.textContent = '';
  const flagged = new Set(unsure);
  text.split(/( +|\n)/).forEach((part) => {
    if (flagged.has(part)) {
      const mark = document.createElement('mark');
      mark.textContent = part;
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
  currentId = ++nextId;
  go.disabled = true;
  setStatus('working');
  worker.postMessage({ id: currentId, text });
}

worker.onmessage = ({ data }) => {
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
