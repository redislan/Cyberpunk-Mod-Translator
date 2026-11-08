'use strict';

// =================================================================================================
// Cyberpunk Mod Translator : interface
// =================================================================================================

const TOKEN = new URLSearchParams(location.search).get('t') || '';
const PAGE_SIZE = 100;
// label : cle du texte dans les fichiers de langue
const STATUS = {
  todo: { label: 'status.todo', color: 'var(--todo)' },
  check: { label: 'status.check', color: 'var(--check)' },
  done: { label: 'status.done', color: 'var(--done)' },
  code: { label: 'status.code', color: 'var(--code)' },
};
const LANGS = ['fr-fr', 'de-de', 'es-es', 'es-mx', 'it-it', 'pl-pl', 'pt-br', 'ru-ru', 'jp-jp', 'kr-kr', 'zh-cn', 'zh-tw',
  'cz-cz', 'hu-hu', 'tr-tr', 'th-th', 'ua-ua', 'ar-ar'];
const TAG_RE = /\{[^{}]*\}|<[^<>]*>|\n|%[-+#0]*\d*(?:\.\d+)?[sdifxXcq]|\\\([^)]*\)/g;

const S = {
  state: null,
  units: [],
  filtered: [],
  fileFilter: null,
  statuses: new Set(['todo', 'check', 'done']),
  search: '',
  onlyEmpty: false,
  page: 0,
  selected: null,
  pending: new Map(),
  saveTimer: null,
  tab: 'translate', // 'translate' ou 'modlist'
};

const $ = (id) => document.getElementById(id);

// -------------------------------------------------------------------------------------------------
// Langue de l'interface : textes des fichiers lang\*.json, servis par /api/lang
// -------------------------------------------------------------------------------------------------

const UI = { code: 'en', setting: '', auto: 'en', strings: {}, languages: [], folder: '' };

/** Texte traduit : t('cle', valeur0, valeur1...). Une cle inconnue s'affiche telle quelle (ca se voit). */
function t(key, ...args) {
  const text = Object.prototype.hasOwnProperty.call(UI.strings, key) ? UI.strings[key] : key;
  return text.replace(/\{(\d+)\}/g, (m, i) => (args[i] !== undefined && args[i] !== null ? String(args[i]) : m));
}

async function loadLanguage() {
  Object.assign(UI, await api('/lang'));
  applyStaticTexts();
}

/** Textes fixes de la page : attributs data-i18n (texte), data-i18n-title (infobulle), data-i18n-placeholder. */
function applyStaticTexts() {
  document.documentElement.lang = UI.code;
  for (const e of document.querySelectorAll('[data-i18n]')) e.textContent = t(e.dataset.i18n);
  for (const e of document.querySelectorAll('[data-i18n-title]')) e.title = t(e.dataset.i18nTitle);
  for (const e of document.querySelectorAll('[data-i18n-placeholder]')) e.placeholder = t(e.dataset.i18nPlaceholder);
}

/** Codes de langue du jeu -> codes standard (noms de langue, correcteur d'orthographe). */
const BCP47 = { 'jp-jp': 'ja', 'kr-kr': 'ko', 'cz-cz': 'cs', 'ua-ua': 'uk', 'zh-cn': 'zh-Hans', 'zh-tw': 'zh-Hant', 'es-mx': 'es-MX', 'pt-br': 'pt-BR' };
const bcp47 = (code) => BCP47[code] || String(code || '').split('-')[0];

/** Nom d'une langue cible dans la langue de l'interface : 'fr-fr' -> « Français », « French »... */
function langName(code) {
  try {
    const n = new Intl.DisplayNames([UI.code], { type: 'language' }).of(bcp47(code));
    if (n && n.toLowerCase() !== bcp47(code).toLowerCase()) return n.charAt(0).toLocaleUpperCase(UI.code) + n.slice(1);
  } catch { /* navigateur sans Intl.DisplayNames */ }
  return code;
}

/** Suffixe des noms de trad : fr-fr -> FR, es-mx -> ES-MX (meme regle que le programme). */
function langSuffix(code) {
  const parts = String(code || '').toUpperCase().split('-');
  return parts.length === 2 && parts[0] === parts[1] ? parts[0] : parts.filter(Boolean).join('-');
}

/** Langue cible : celle du mod ouvert, sinon celle des Parametres. */
function targetLang() {
  const st = S.state;
  return (st && st.project && st.project.lang) || (st && st.settings && st.settings.lang) || 'fr-fr';
}

/** Cree un element : el('div', {class: 'x', onclick: fn}, 'texte', autreElement) */
function el(tag, attrs, ...children) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (k === 'class') e.className = v;
    else if (k === 'text') e.textContent = v;
    else if (k === 'value') e.value = v;
    else e.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) {
    if (c === null || c === undefined || c === false) continue;
    e.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return e;
}

// -------------------------------------------------------------------------------------------------
// Communication avec le programme
// -------------------------------------------------------------------------------------------------

async function api(path, body) {
  const opts = { method: body === undefined ? 'GET' : 'POST', headers: { 'X-Token': TOKEN } };
  if (body !== undefined) {
    opts.headers['Content-Type'] = 'application/json';
    opts.body = JSON.stringify(body);
  }
  const r = await fetch('/api' + path, opts);
  const txt = await r.text();
  let data = null;
  if (txt) {
    try { data = JSON.parse(txt); } catch { data = null; }
  }
  if (!r.ok) {
    const err = new Error((data && data.error) || t('error.http', r.status));
    err.fromApi = true; // deja ecrite dans le journal par le programme
    throw err;
  }
  return data;
}

/**
 * Ecrit une erreur de la page dans le journal de session du programme.
 * Les erreurs renvoyees par le programme (fromApi) y sont deja : on ne les double pas.
 */
function reportError(e, where) {
  if (e && e.fromApi) return;
  const detail = e && e.stack ? e.stack : String(e && e.message ? e.message : e);
  const message = `${where || 'page'} : ${detail}`;
  console.error(message);
  fetch('/api/log', {
    method: 'POST',
    headers: { 'X-Token': TOKEN, 'Content-Type': 'application/json' },
    body: JSON.stringify({ level: 'error', message }),
  }).catch(() => { /* programme injoignable : l'erreur reste dans la console */ });
}

window.addEventListener('error', (ev) => reportError(ev.error || ev.message, `${ev.filename || ''}:${ev.lineno || 0}`));
window.addEventListener('unhandledrejection', (ev) => reportError(ev.reason, 'promesse'));

async function busy(text, fn) {
  $('busyText').textContent = text;
  $('busy').classList.remove('hidden');
  try {
    return await fn();
  } catch (e) {
    reportError(e, text);
    toast(e.message, 'error');
    return null;
  } finally {
    $('busy').classList.add('hidden');
  }
}

function toast(message, kind) {
  const node = el('div', { class: `toast ${kind || ''}` }, message);
  $('toasts').append(node);
  setTimeout(() => node.remove(), kind === 'error' ? 9000 : 5000);
}

// -------------------------------------------------------------------------------------------------
// Sauvegarde automatique
// -------------------------------------------------------------------------------------------------

function setSaveStatus(kind) {
  const sb = $('sbSave');
  sb.className = kind === 'saved' ? 'saved' : kind === 'pending' ? 'pending' : '';
  sb.textContent = kind === 'saved' ? t('save.saved') : kind === 'pending' ? t('save.pending')
    : kind === 'error' ? t('save.error') : '';
}

function queueSave(u) {
  S.pending.set(u.id, u.fr);
  setSaveStatus('pending');
  clearTimeout(S.saveTimer);
  S.saveTimer = setTimeout(flush, 700);
}

async function flush() {
  clearTimeout(S.saveTimer);
  if (S.pending.size === 0) return true;
  const items = [...S.pending].map(([id, fr]) => ({ id, fr }));
  S.pending.clear();
  try {
    await api('/translations', items);
    if (S.pending.size === 0) setSaveStatus('saved');
    return true;
  } catch (e) {
    reportError(e, 'sauvegarde');
    for (const it of items) if (!S.pending.has(it.id)) S.pending.set(it.id, it.fr);
    setSaveStatus('error');
    toast(t('save.failed', e.message), 'error');
    return false;
  }
}

function flushOnExit() {
  if (S.pending.size > 0) {
    const items = [...S.pending].map(([id, fr]) => ({ id, fr }));
    navigator.sendBeacon(`/api/translations?t=${TOKEN}`, new Blob([JSON.stringify(items)], { type: 'application/json' }));
    S.pending.clear();
  }
  if (ML.prefsDirty) {
    navigator.sendBeacon(`/api/modlist/prefs?t=${TOKEN}`, new Blob([JSON.stringify(ML.prefs)], { type: 'application/json' }));
    ML.prefsDirty = false;
  }
  navigator.sendBeacon(`/api/bye?t=${TOKEN}`, new Blob(['{}'], { type: 'application/json' }));
}

// -------------------------------------------------------------------------------------------------
// Etat general
// -------------------------------------------------------------------------------------------------

async function refreshState() {
  S.state = await api('/state');
  renderHeader();
}

function renderHeader() {
  const st = S.state;
  const p = st.project;
  $('modName').textContent = p ? t('header.modInfo', p.name, p.files.length) : t('header.noMod');
  const target = targetLang();
  $('targetHead').textContent = langName(target);
  $('welcomeStep2').textContent = t('welcome.step2.text', langName(target));
  $('welcomeStep3').textContent = t('welcome.step3.text', p && p.suffix ? p.suffix : langSuffix(target));
  for (const b of document.querySelectorAll('.needs-project')) {
    if (b.tagName === 'BUTTON') b.disabled = !p;
    else b.querySelectorAll('button').forEach((x) => { x.disabled = !p; });
  }
  const translating = S.tab === 'translate';
  $('welcome').classList.toggle('hidden', !!p || !translating);
  $('workspace').classList.toggle('hidden', !p || !translating);
  $('modlistView').classList.toggle('hidden', translating);

  const banner = $('banner');
  if (st.compressorError) {
    banner.replaceChildren(
      t('banner.noDll'),
      el('button', { class: 'btn small', onclick: openSettings }, t('settings.title')));
    banner.classList.remove('hidden');
  } else {
    banner.classList.add('hidden');
  }
  $('sbCompressor').textContent = st.compressor ? t('statusbar.dllFound') : t('statusbar.dllMissing');
  $('sbCompressor').title = st.compressor || st.compressorError || '';

  const recent = $('recent');
  recent.replaceChildren();
  if (!p && st.settings.recentMods.length) {
    recent.append(el('h3', {}, t('welcome.recent')));
    for (const path of st.settings.recentMods) recent.append(el('button', { title: path, onclick: () => openMod(path) }, path));
  }
}

// -------------------------------------------------------------------------------------------------
// Projet : chargement et filtres
// -------------------------------------------------------------------------------------------------

async function loadUnits() {
  S.units = await api('/units');
  S.fileFilter = null;
  S.page = 0;
  S.selected = null;
  renderFiles();
  renderNotes();
  applyFilters();
  renderDetail(null);
}

const isTranslated = (u) => u.fr.trim() !== '';
const fileKey = (u) => `${u.src}|${u.file}`;

function renderFiles() {
  const box = $('files');
  box.replaceChildren();
  const p = S.state.project;
  const showCode = S.statuses.has('code');
  const visible = S.units.filter((u) => showCode || u.status !== 'code').length;
  const all = el('button', { class: `file ${S.fileFilter === null ? 'active' : ''}`, onclick: () => setFileFilter(null) },
    el('span', { class: 'fname' }, t('files.all')),
    el('span', { class: 'fmeta' }, t('files.lines', visible)));
  box.append(all);
  let hidden = 0;
  for (const f of p.files) {
    const key = `${f.src}|${f.file}`;
    const units = S.units.filter((u) => fileKey(u) === key && u.status !== 'code');
    if (units.length === 0 && !showCode) {
      hidden++;
      continue;
    }
    const done = units.filter(isTranslated).length;
    const name = f.file.split(/[\\/]/).pop();
    const where = f.src === '-' ? t('files.kind.file') : `${t(`files.kind.${f.kind}`)} · ${f.src}`;
    box.append(el('button', {
      class: `file ${S.fileFilter === key ? 'active' : ''}`,
      title: `${f.src === '-' ? '' : f.src + ' → '}${f.file}\n${f.reason}`,
      onclick: () => setFileFilter(key),
    },
    el('span', { class: 'fname' }, name),
    el('span', { class: 'fmeta' }, t('files.progress', where, done, units.length))));
  }
  if (hidden) {
    box.append(el('div', { class: 'fmeta', style: 'padding:6px 9px;color:var(--text-dim);font-size:11px' },
      t('files.hidden', hidden)));
  }
  renderProgress();
}

function renderNotes() {
  const p = S.state.project;
  const notes = $('notes');
  notes.replaceChildren();
  for (const n of p.notes) notes.append(el('div', {}, '⚠ ' + n));
  if (p.archivesWithoutText.length) notes.append(el('div', {}, t('notes.noText', p.archivesWithoutText.join(', '))));
}

function renderProgress() {
  const todo = S.units.filter((u) => u.status === 'todo');
  const done = todo.filter(isTranslated).length;
  const pct = todo.length ? Math.floor((done * 100) / todo.length) : 100;
  $('progressText').textContent = `${pct} %`;
  $('progressFill').style.width = `${pct}%`;
  const check = S.units.filter((u) => u.status === 'check').length;
  const already = S.units.filter((u) => u.status === 'done').length;
  $('progressDetail').textContent = t('progress.detail', done, todo.length, check, already);
  $('sbCount').textContent = t('statusbar.count', S.units.filter((u) => u.status !== 'code').length, S.units.filter((u) => u.status === 'code').length);
}

function setFileFilter(key) {
  S.fileFilter = key;
  S.page = 0;
  renderFiles();
  applyFilters();
}

function renderChips() {
  const chips = $('chips');
  chips.replaceChildren();
  const base = S.units.filter((u) => S.fileFilter === null || fileKey(u) === S.fileFilter);
  for (const [key, info] of Object.entries(STATUS)) {
    const count = base.filter((u) => u.status === key).length;
    if (count === 0 && key !== 'todo') continue;
    chips.append(el('button', {
      class: `chip ${S.statuses.has(key) ? 'on' : ''}`,
      title: key === 'code' ? t('chip.code.tip') : '',
      onclick: () => {
        if (S.statuses.has(key)) S.statuses.delete(key); else S.statuses.add(key);
        S.page = 0;
        if (key === 'code') renderFiles();
        applyFilters();
      },
    }, el('span', { class: 'dot', style: `background:${info.color}` }), `${t(info.label)} (${count})`));
  }
}

function applyFilters() {
  const q = S.search.trim().toLowerCase();
  S.filtered = S.units.filter((u) =>
    (S.fileFilter === null || fileKey(u) === S.fileFilter) &&
    S.statuses.has(u.status) &&
    (!S.onlyEmpty || !isTranslated(u)) &&
    (!q || u.en.toLowerCase().includes(q) || u.fr.toLowerCase().includes(q) || u.key.toLowerCase().includes(q)));
  const pages = Math.max(1, Math.ceil(S.filtered.length / PAGE_SIZE));
  if (S.page >= pages) S.page = pages - 1;
  renderChips();
  renderTable();
}

// -------------------------------------------------------------------------------------------------
// Tableau
// -------------------------------------------------------------------------------------------------

function tags(text) {
  return (text.match(TAG_RE) || []).slice().sort();
}

function tagProblem(u) {
  if (!isTranslated(u)) return null;
  const a = tags(u.en);
  const b = tags(u.fr);
  if (a.length === b.length && a.every((x, i) => x === b[i])) return null;
  const show = (l) => l.map((x) => (x === '\n' ? '\\n' : x)).join(' ') || t('tags.none');
  return t('tags.problem', show(a), show(b));
}

function statusBadge(u) {
  const main = u.status === 'todo' && isTranslated(u)
    ? el('span', { class: 'status translated' }, t('status.translated'))
    : el('span', { class: `status ${u.status}` }, t(STATUS[u.status].label));
  if (!u.links) return main;
  const on = u.applyLinks;
  return el('div', { class: 'badges' }, main,
    el('span', {
      class: `status link ${on ? '' : 'off'}`,
      title: on ? t('link.on.tip') : t('link.off.tip'),
    }, on ? t('link.on') : t('link.off')));
}

function autosize(ta) {
  ta.style.height = 'auto';
  ta.style.height = `${ta.scrollHeight + 2}px`;
}

function renderTable() {
  const table = $('table');
  const start = S.page * PAGE_SIZE;
  const rows = S.filtered.slice(start, start + PAGE_SIZE);
  table.replaceChildren();
  if (rows.length === 0) {
    table.append(el('div', { class: 'empty' }, t('table.empty')));
  }
  for (const u of rows) table.append(rowEl(u));
  requestAnimationFrame(() => table.querySelectorAll('textarea').forEach(autosize));
  table.scrollTop = 0;
  renderPager();
}

function rowEl(u) {
  const warn = el('div', { class: 'warnmsg' });
  const ta = el('textarea', { rows: 1, spellcheck: 'true', lang: bcp47(targetLang()), 'data-id': u.id, value: u.fr });
  const badgeBox = el('div', {}, statusBadge(u));
  const row = el('div', { class: `row ${S.selected === u.id ? 'selected' : ''}`, 'data-id': u.id },
    el('div', { class: 'num' }, u.id + 1),
    badgeBox,
    el('div', { class: 'en' }, u.en),
    el('div', { class: 'fr' }, ta, warn));

  const refreshWarn = () => {
    const pb = tagProblem(u);
    ta.classList.toggle('warn', !!pb);
    warn.textContent = pb || '';
  };
  refreshWarn();

  ta.addEventListener('input', () => {
    u.fr = ta.value;
    autosize(ta);
    refreshWarn();
    badgeBox.replaceChildren(statusBadge(u));
    queueSave(u);
    renderProgressSoon();
    if (S.selected === u.id) renderDetail(u);
  });
  ta.addEventListener('focus', () => select(u));
  ta.addEventListener('change', () => fillIdentical(u));
  ta.addEventListener('blur', () => flush());
  ta.addEventListener('keydown', (e) => {
    if ((e.ctrlKey && e.key === 'Enter') || (e.altKey && e.key === 'ArrowDown')) {
      e.preventDefault();
      focusRelative(u, 1);
    } else if (e.altKey && e.key === 'ArrowUp') {
      e.preventDefault();
      focusRelative(u, -1);
    }
  });
  row.addEventListener('click', (e) => {
    if (e.target !== ta) ta.focus();
  });
  return row;
}

/**
 * Le meme texte anglais revient souvent (categorie « General » de Mod Settings, variantes F/M...) :
 * quand une ligne est traduite, on remplit les lignes identiques encore vides. Elles restent modifiables.
 */
function fillIdentical(u) {
  if (!isTranslated(u)) return;
  const others = S.units.filter((x) => x !== u && x.en === u.en && x.status !== 'code' && x.status !== 'done' && !isTranslated(x));
  if (others.length === 0) return;
  for (const x of others) {
    x.fr = u.fr;
    queueSave(x);
    const ta = $('table').querySelector(`textarea[data-id="${x.id}"]`);
    if (ta) {
      ta.value = u.fr;
      autosize(ta);
      const row = ta.closest('.row');
      if (row) row.children[1].replaceChildren(statusBadge(x));
    }
  }
  renderProgressSoon();
  toast(t('fill.identical', others.length), 'ok');
}

let progressTimer = null;
function renderProgressSoon() {
  clearTimeout(progressTimer);
  progressTimer = setTimeout(() => {
    renderProgress();
    renderFilesCounts();
  }, 400);
}

function renderFilesCounts() {
  // met a jour les compteurs de la liste des fichiers sans la reconstruire
  const scroll = $('files').scrollTop;
  renderFiles();
  $('files').scrollTop = scroll;
}

function renderPager() {
  const pager = $('pager');
  pager.replaceChildren();
  const total = S.filtered.length;
  const pages = Math.ceil(total / PAGE_SIZE);
  if (pages <= 1) return;
  const go = (p) => {
    S.page = Math.max(0, Math.min(pages - 1, p));
    renderTable();
  };
  const from = S.page * PAGE_SIZE + 1;
  const to = Math.min(total, from + PAGE_SIZE - 1);
  pager.append(
    el('button', { class: 'btn small', onclick: () => go(0), disabled: S.page === 0 }, '«'),
    el('button', { class: 'btn small', onclick: () => go(S.page - 1), disabled: S.page === 0 }, '‹'),
    el('span', {}, t('pager', from, to, total, S.page + 1, pages)),
    el('button', { class: 'btn small', onclick: () => go(S.page + 1), disabled: S.page >= pages - 1 }, '›'),
    el('button', { class: 'btn small', onclick: () => go(pages - 1), disabled: S.page >= pages - 1 }, '»'));
}

function focusRelative(u, dir) {
  const idx = S.filtered.indexOf(u);
  const target = S.filtered[idx + dir];
  if (!target) return;
  const page = Math.floor((idx + dir) / PAGE_SIZE);
  if (page !== S.page) {
    S.page = page;
    renderTable();
  }
  const ta = $('table').querySelector(`textarea[data-id="${target.id}"]`);
  if (ta) {
    ta.focus();
    ta.scrollIntoView({ block: 'nearest' });
  }
}

function select(u) {
  if (S.selected === u.id) return;
  S.selected = u.id;
  for (const r of $('table').querySelectorAll('.row')) r.classList.toggle('selected', Number(r.dataset.id) === u.id);
  renderDetail(u);
}

function setTranslation(u, text) {
  u.fr = text;
  const ta = $('table').querySelector(`textarea[data-id="${u.id}"]`);
  if (ta) {
    ta.value = text;
    ta.dispatchEvent(new Event('input'));
    ta.focus();
  } else {
    queueSave(u);
    renderProgressSoon();
  }
}

function renderDetail(u) {
  const d = $('detail');
  d.replaceChildren();
  if (!u) {
    d.append(el('h3', {}, t('detail.title')),
      el('div', { class: 'hint' }, t('detail.hint')),
      shortcuts());
    return;
  }
  const field = (label, value, mono) => el('div', { class: 'field' },
    el('div', { class: 'label' }, label),
    el('div', { class: mono ? 'mono' : 'value' }, value));
  const variant = u.field === 'F' ? t('detail.variant.F') : u.field === 'M' ? t('detail.variant.M') : null;
  const enTags = tags(u.en).map((x) => (x === '\n' ? t('detail.newline') : x));
  d.append(...[
    el('h3', {}, t('detail.line', u.id + 1)),
    field(t('detail.status'), t(STATUS[u.status].label)),
    field(t('detail.file'), u.src === '-' ? u.file : `${u.src} → ${u.file}`),
    field(t('detail.key'), u.key),
    variant ? field(t('detail.variant'), variant) : null,
    u.context ? field(t('detail.context'), u.context, true) : null,
    field(t('detail.source'), u.en, true),
    enTags.length ? field(t('detail.tags'), enTags.join('   '), true) : null,
    u.status === 'done' && u.current !== u.fr ? field(t('detail.author'), u.current, true) : null,
    u.links ? linkBox(u) : null,
    u.linkedTo ? el('div', { class: 'linkbox' },
      el('div', { class: 'label' }, t('detail.linkedTo.title')),
      el('div', {}, t('detail.linkedTo.text', u.linkedTo.join(', ')))) : null,
    el('div', { class: 'buttons' },
      el('button', { class: 'btn small', onclick: () => setTranslation(u, u.en) }, t('detail.copySource')),
      el('button', { class: 'btn small', onclick: () => setTranslation(u, u.status === 'done' ? u.current : '') }, t('detail.reset'))),
    shortcuts()].filter(Boolean));
}

function linkBox(u) {
  const cb = el('input', { type: 'checkbox', checked: u.applyLinks });
  cb.addEventListener('change', async () => {
    try {
      await api('/links', { id: u.id, apply: cb.checked });
      u.applyLinks = cb.checked;
      const row = $('table').querySelector(`.row[data-id="${u.id}"]`);
      if (row) row.children[1].replaceChildren(statusBadge(u));
    } catch (e) {
      cb.checked = !cb.checked;
      toast(e.message, 'error');
    }
  });
  return el('div', { class: 'linkbox' },
    el('div', { class: 'label' }, t('link.box.title')),
    el('div', {}, t('link.box.text')),
    el('ul', { class: 'mono' }, ...u.links.map((l) => el('li', {}, l))),
    u.same && u.same.length ? el('div', { class: 'same' },
      t(u.same.length > 1 ? 'link.same.many' : 'link.same.one', u.same.map((x) => `${x.line} (${x.where})`).join(', '))) : null,
    el('label', { class: 'link-opt' }, cb, t('link.apply')));
}

function shortcuts() {
  return el('div', { class: 'hint' },
    el('div', {}, el('span', { class: 'kbd' }, t('keys.ctrlEnter')), t('keys.or'), el('span', { class: 'kbd' }, 'Alt+↓'), t('keys.next')),
    el('div', {}, el('span', { class: 'kbd' }, 'Alt+↑'), t('keys.prev')),
    el('div', {}, el('span', { class: 'kbd' }, 'Ctrl+S'), t('keys.save'), el('span', { class: 'kbd' }, 'Ctrl+F'), t('keys.search')),
    el('div', { style: 'margin-top:8px' }, t('keys.tagsHint')));
}

// -------------------------------------------------------------------------------------------------
// Fenetres
// -------------------------------------------------------------------------------------------------

function closeModal() {
  $('modal').classList.add('hidden');
  $('dialog').replaceChildren();
}

function showModal(title, body, buttons, left) {
  const footer = el('footer', {}, left ? el('span', { class: 'left' }, left) : null, ...buttons);
  $('dialog').replaceChildren(el('header', {}, title), el('div', { class: 'body' }, body), footer);
  $('modal').classList.remove('hidden');
}

/**
 * Choix d'un chemin avec la vraie fenetre « Ouvrir » de Windows.
 * mode : 'mod' (dossier ou .zip, on demande lequel), 'folder' (dossier), 'file' (fichier).
 * key : sert a Windows pour retenir le dernier dossier utilise pour cet usage.
 * Si la fenetre Windows n'est pas disponible, on se rabat sur l'explorateur integre.
 */
async function choosePath(opts) {
  if (!S.state || !S.state.nativeDialogs) return pickPath(opts);
  let { mode, ext, start, key } = opts;
  if (mode === 'mod') {
    const kind = await chooseModKind(opts.title, opts.intro);
    if (!kind) return null;
    if (kind === 'builtin') return pickPath(opts);
    mode = kind;
    key = `${key || 'mod'}-${kind === 'folder' ? 'dossier' : 'zip'}`;
    if (kind === 'file') {
      ext = '.zip';
      start = opts.zipStart || '';
    }
  }
  const r = await busy(t('dialog.busy'),
    () => api('/dialog', {
      mode, title: opts.title, start: start || '', ext: ext || '', okLabel: opts.okLabel || '', key: key || mode, fallback: opts.fallback || '',
    }));
  if (!r) return null;
  if (r.busy) {
    toast(t('dialog.alreadyOpen'), 'error');
    return null;
  }
  if (r.unsupported) {
    toast(t('dialog.unsupported', r.error ? ` (${r.error})` : ''), 'error');
    return pickPath(opts);
  }
  return r.path || null;
}

/** Un mod, c'est un dossier (MO2) ou un .zip (Nexus) : Windows ne sait pas proposer les deux a la fois. */
function chooseModKind(title, intro) {
  return new Promise((resolve) => {
    const done = (v) => {
      closeModal();
      resolve(v);
    };
    const tile = (icon, head, sub, value) => el('button', { class: 'choice', onclick: () => done(value) },
      el('span', { class: 'big' }, icon), el('b', {}, head), el('span', {}, sub));
    showModal(title,
      el('div', {},
        el('p', { class: 'help' }, intro || t('modKind.intro')),
        el('div', { class: 'choice-grid' },
          tile('📁', t('modKind.folder'), t('modKind.folder.sub'), 'folder'),
          tile('📦', t('modKind.zip'), t('modKind.zip.sub'), 'file'))),
      [el('button', { class: 'btn', onclick: () => done(null) }, t('btn.cancel'))],
      el('a', { href: '#', class: 'quiet', onclick: (e) => { e.preventDefault(); done('builtin'); } }, t('modKind.builtin')));
  });
}

/** Explorateur integre (secours). mode : 'mod' (dossier ou .zip), 'folder' (dossier), 'file' (fichier). */
function pickPath({ title, mode, ext, start, okLabel }) {
  return new Promise((resolve) => {
    let current = '';
    let selected = null;
    const storeKey = `cmt.lastDir.${mode}`;
    const pathInput = el('input', { type: 'text', placeholder: t('picker.path') });
    const entriesBox = el('div', { class: 'entries' });
    const placesBox = el('div', { class: 'places' });
    const info = el('span', {});
    const okBtn = el('button', { class: 'btn primary' }, okLabel || t('picker.choose'));

    const done = (value) => {
      if (value) {
        try { localStorage.setItem(storeKey, current); } catch { /* rien */ }
      }
      closeModal();
      resolve(value);
    };

    const updateInfo = () => {
      if (selected) info.textContent = t('picker.selection', selected.name);
      else if (mode !== 'file' && current) info.textContent = t('picker.current', current);
      else info.textContent = mode === 'file' ? t('picker.pickFile') : '';
      okBtn.disabled = mode === 'file' ? !(selected && selected.type === 'file') : !(selected || current);
    };

    const go = async (path) => {
      try {
        const data = await api(`/browse?path=${encodeURIComponent(path || '')}&ext=${encodeURIComponent(ext || '.zip')}`);
        current = data.path;
        selected = null;
        pathInput.value = data.path;
        placesBox.replaceChildren(...data.places.map((p) => el('button', { title: p.path, onclick: () => go(p.path) }, '📁 ' + p.name)));
        entriesBox.replaceChildren();
        if (data.parent !== null && data.parent !== undefined) {
          entriesBox.append(el('div', { class: 'entry', ondblclick: () => go(data.parent), onclick: () => go(data.parent) },
            el('span', { class: 'ico' }, '↰'), t('picker.parent')));
        }
        for (const e of data.entries) {
          const row = el('div', { class: 'entry', title: e.path },
            el('span', { class: 'ico' }, e.type === 'file' ? '📦' : e.type === 'drive' ? '💽' : '📁'),
            e.name,
            e.isMod ? el('span', { class: 'tag' }, t('picker.modTag')) : null);
          row.addEventListener('click', () => {
            if (e.type === 'drive') return go(e.path);
            selected = e;
            entriesBox.querySelectorAll('.entry').forEach((x) => x.classList.remove('selected'));
            row.classList.add('selected');
            updateInfo();
          });
          row.addEventListener('dblclick', () => {
            if (e.type === 'file') done(e.path);
            else go(e.path);
          });
          entriesBox.append(row);
        }
        if (data.entries.length === 0) entriesBox.append(el('div', { class: 'empty' }, t('picker.empty')));
        updateInfo();
      } catch (err) {
        toast(err.message, 'error');
      }
    };

    okBtn.addEventListener('click', () => {
      if (mode === 'file') return done(selected && selected.type === 'file' ? selected.path : null);
      done(selected ? selected.path : current);
    });
    pathInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') go(pathInput.value); });

    const help = mode === 'mod'
      ? t('picker.help.mod')
      : mode === 'folder' ? t('picker.help.folder') : '';

    showModal(title,
      el('div', {},
        help ? el('div', { class: 'form-row' }, el('div', { class: 'help' }, help)) : null,
        el('div', { class: 'picker' },
          placesBox,
          el('div', {},
            el('div', { class: 'pathbar' }, pathInput, el('button', { class: 'btn', onclick: () => go(pathInput.value) }, t('picker.go'))),
            entriesBox))),
      [el('button', { class: 'btn', onclick: () => done(null) }, t('btn.cancel')), okBtn],
      info);

    let startPath = start || '';
    try { startPath = startPath || localStorage.getItem(storeKey) || ''; } catch { /* rien */ }
    go(startPath).then(() => { if (!current) go(''); });
  });
}

// -------------------------------------------------------------------------------------------------
// Actions
// -------------------------------------------------------------------------------------------------

async function openMod(path) {
  if (!path) {
    path = await choosePath({ title: t('btn.open'), mode: 'mod', ext: '.zip', start: S.state.settings.mo2ModsDir, okLabel: t('open.ok'), key: 'mod' });
    if (!path) return false;
  }
  // Modifications non enregistrees : on reste sur ce mod (elles sont reperees par numero de ligne,
  // elles atterriraient dans le mauvais mod). flush() a deja affiche l'erreur.
  if (!(await flush())) return false;
  const st = await busy(t('open.busy'), () => api('/open', { path }));
  if (!st) return false;
  S.pending.clear();
  S.state = st;
  S.tab = 'translate';
  applyTab();
  await loadUnits();
  const p = st.project;
  toast(t('open.done', p.name, S.units.filter((u) => u.status !== 'code').length, p.files.length), 'ok');
  if (p.restored > 0) toast(t('open.restored', p.restored), 'ok');
  return true;
}

async function importTranslation() {
  const path = await choosePath({
    title: t('importTrad.title'), intro: t('importTrad.intro'), mode: 'mod', ext: '.zip', okLabel: t('importTrad.ok'), key: 'trad',
  });
  if (!path) return;
  await flush();
  const r = await busy(t('importTrad.busy'), () => api('/import-translation', { path }));
  if (!r) return;
  await loadUnits();
  toast(t('importTrad.done', r.count), r.count ? 'ok' : '');
}

async function exportTsv() {
  await flush();
  const r = await busy(t('exportTsv.busy'), () => api('/export-tsv', { path: S.state.settings.outputDir }));
  if (!r) return;
  showModal(t('exportTsv.title'),
    el('div', {},
      el('p', {}, t('exportTsv.text')),
      el('div', { class: 'mono', style: 'padding:8px;background:var(--bg-3);border-radius:6px;word-break:break-all' }, r.file)),
    [el('button', { class: 'btn', onclick: () => api('/reveal', { path: r.file }) }, t('btn.openFolder')),
      el('button', { class: 'btn primary', onclick: closeModal }, t('btn.ok'))]);
}

async function importTsv() {
  const path = await choosePath({
    title: t('importTsv.title'), mode: 'file', ext: '.tsv,.txt', okLabel: t('importTsv.ok'), key: 'tsv', fallback: S.state.settings.outputDir,
  });
  if (!path) return;
  await flush();
  const r = await busy(t('importTsv.busy'), () => api('/import-tsv', { path }));
  if (!r) return;
  await loadUnits();
  toast(t('importTsv.done', r.count), 'ok');
  if (r.warnings && r.warnings.length) toast(t('importTsv.warn', r.warnings.length), 'error');
}

async function save() {
  await flush();
  const r = await busy(t('save.busy'), () => api('/save', {}));
  if (r) {
    setSaveStatus('saved');
    toast(t('save.done'), 'ok');
  }
}

/** Reaffiche toute la page dans la langue qui vient d'etre choisie. */
function relocalize() {
  applyStaticTexts();
  renderHeader();
  if (S.tab === 'modlist') mlRender();
  if (S.state && S.state.project) {
    renderFiles();
    renderNotes();
    applyFilters();
    renderDetail(S.units.find((u) => u.id === S.selected) || null);
  } else {
    renderDetail(null);
  }
}

async function openSettings() {
  // Relit le dossier des langues : un fichier qu'on vient d'y poser apparait tout de suite
  try {
    await loadLanguage();
  } catch (e) {
    toast(e.message, 'error');
  }
  const st = S.state.settings;
  const input = (value) => el('input', { type: 'text', value: value || '' });
  const game = input(st.gameDir);
  const mo2 = input(st.mo2ModsDir);
  const out = input(st.outputDir);
  const lang = el('select', {}, ...LANGS.map((l) => el('option', { value: l, selected: l === st.lang }, `${langName(l)} (${l})`)));

  // Langue de l'interface : automatique (langue de Windows) ou une langue trouvee (integree ou dossier lang)
  const usable = UI.languages.filter((l) => !l.error);
  const nameOf = (code) => (usable.find((l) => l.code === code) || { name: code }).name;
  const uiLang = el('select', {},
    el('option', { value: '', selected: !UI.setting }, t('settings.uiLang.auto', nameOf(UI.auto))),
    ...usable.map((l) => el('option', { value: l.code, selected: l.code === UI.setting },
      l.percent < 100 ? `${l.name} — ${l.percent} %` : l.name)));
  const credit = el('div', { class: 'help' });
  const updateCredit = () => {
    const l = usable.find((x) => x.code === (uiLang.value || UI.auto));
    // l'anglais est la langue d'origine : pas de credit de traduction
    credit.textContent = l && l.translator && l.code !== 'en' ? t('settings.uiLang.by', l.translator) : '';
  };
  uiLang.addEventListener('change', updateCredit);
  updateCredit();
  const broken = UI.languages.filter((l) => l.error).map((l) => el('div', { class: 'help warn' },
    '⚠ ' + t('settings.uiLang.broken', l.file, l.error === 'badname' ? t('settings.uiLang.badName') : l.error)));

  // Le selecteur de dossier remplace temporairement la fenetre : on reaffiche les reglages ensuite
  // (les champs sont les memes objets, leurs valeurs sont conservees).
  const browse = (target, title, key) => el('button', {
    class: 'btn',
    onclick: async () => {
      const p = await choosePath({ title, mode: 'folder', start: target.value, okLabel: t('settings.chooseFolder'), key });
      if (p) target.value = p;
      build();
    },
  }, t('settings.browse'));
  const build = () => {
    showModal(t('settings.title'),
      el('div', {},
        el('div', { class: 'form-row' }, el('label', {}, t('settings.uiLang')), uiLang, credit,
          ...broken,
          el('div', { class: 'lang-tools' },
            el('div', { class: 'help' }, t('settings.uiLang.help')),
            el('button', { class: 'btn small', title: UI.folder, onclick: () => api('/reveal-lang', {}).catch((e) => toast(e.message, 'error')) },
              t('settings.uiLang.folder')))),
        el('hr', { class: 'settings-sep' }),
        el('div', { class: 'form-row' }, el('label', {}, t('settings.game')),
          el('div', { class: 'inline' }, game, browse(game, t('settings.game'), 'jeu')),
          el('div', { class: 'help' }, t('settings.game.help'))),
        el('div', { class: 'form-row' }, el('label', {}, t('settings.mo2')),
          el('div', { class: 'inline' }, mo2, browse(mo2, t('settings.mo2'), 'mo2')),
          el('div', { class: 'help' }, t('settings.mo2.help'))),
        el('div', { class: 'form-row' }, el('label', {}, t('settings.output')),
          el('div', { class: 'inline' }, out, browse(out, t('settings.output.title'), 'sortie'))),
        el('div', { class: 'form-row' }, el('label', {}, t('settings.target')), lang,
          el('div', { class: 'help' }, t('settings.target.help'))),
        el('div', { class: 'form-row' }, el('label', {}, t('settings.logs')),
          el('div', { class: 'lang-tools' },
            el('div', { class: 'help' }, t('settings.logs.help')),
            el('button', { class: 'btn small', onclick: () => api('/reveal-logs', {}).catch((e) => toast(e.message, 'error')) },
              t('settings.logs.open')))),
        el('div', { class: 'form-row' }, el('div', { class: 'help' }, t('settings.data', S.state.dataDir)))),
      [el('button', { class: 'btn', onclick: closeModal }, t('btn.cancel')),
        el('button', {
          class: 'btn primary',
          onclick: async () => {
            const langChanged = uiLang.value !== (UI.setting || '');
            const st2 = await busy(t('save.busy'), () => api('/settings', {
              gameDir: game.value, mo2ModsDir: mo2.value, outputDir: out.value, lang: lang.value, uiLang: uiLang.value, recentMods: st.recentMods,
            }));
            if (!st2) return;
            S.state = st2;
            closeModal();
            // dossier MO2, langue cible ou dossier de sortie ont pu changer : la liste est relue
            if (S.tab === 'modlist') mlLoad({});
            if (langChanged) {
              try {
                await loadLanguage();
              } catch (e) {
                toast(e.message, 'error');
              }
              relocalize();
            } else {
              renderHeader();
            }
            toast(st2.compressor ? t('settings.saved') : t('settings.savedNoDll'), st2.compressor ? 'ok' : 'error');
          },
        }, t('btn.save'))]);
  };
  build();
}

async function buildTranslation() {
  await flush();
  const p = S.state.project;
  const changed = S.units.filter((u) => isTranslated(u) && u.fr !== u.current).length;
  const warns = S.units.filter((u) => tagProblem(u)).length;
  const name = el('input', { type: 'text', value: `${p.name} - ${p.suffix || langSuffix(p.lang)}` });
  const out = el('input', { type: 'text', value: S.state.settings.outputDir });
  const browseBtn = el('button', { class: 'btn' }, t('settings.browse'));
  const render = () => showModal(t('btn.build'),
    el('div', {},
      el('p', {}, t('build.count', changed),
        warns ? el('span', { style: 'color:var(--warn)' }, t('build.tagWarn', warns)) : null),
      el('div', { class: 'form-row' }, el('label', {}, t('build.name')), name),
      el('div', { class: 'form-row' }, el('label', {}, t('build.output')), el('div', { class: 'inline' }, out, browseBtn))),
    [el('button', { class: 'btn', onclick: closeModal }, t('btn.cancel')),
      el('button', { class: 'btn primary', onclick: run, disabled: changed === 0 }, t('build.run'))]);
  browseBtn.addEventListener('click', async () => {
    const dir = await choosePath({ title: t('build.output'), mode: 'folder', start: out.value, okLabel: t('settings.chooseFolder'), key: 'sortie' });
    if (dir) out.value = dir;
    render();
  });
  async function run() {
    closeModal();
    const r = await busy(t('build.busy'), () => api('/build', { path: out.value, name: name.value }));
    if (!r) return;
    await refreshState();
    showModal(t('build.doneTitle'),
      el('div', {},
        el('div', { class: 'ok-big' }, t('build.done', r.changed)),
        el('p', {}, t('build.install')),
        el('div', { class: 'mono', style: 'padding:8px;background:var(--bg-3);border-radius:6px;word-break:break-all' }, r.zip || ''),
        el('p', { style: 'margin-top:14px;color:var(--text-dim)' }, t('build.content')),
        el('ul', { class: 'result-list' },
          ...r.archives.map((a) => el('li', {}, t('build.archive', a.split(/[\\/]/).pop()))),
          ...r.plainFiles.map((f) => el('li', {}, t('build.file', f.split(/[\\/]/).pop())))),
        r.infos && r.infos.length ? el('div', {},
          el('p', { style: 'margin-top:14px;color:var(--text-dim)' }, t('build.infos')),
          el('ul', { class: 'result-list' }, ...r.infos.slice(0, 40).map((w) => el('li', {}, w)))) : null,
        r.warnings.length ? el('div', {},
          el('p', { style: 'margin-top:14px;color:var(--warn)' }, t('build.warnings')),
          el('ul', { class: 'result-list warn-list' }, ...r.warnings.slice(0, 40).map((w) => el('li', {}, w)))) : null),
      [el('button', { class: 'btn', onclick: () => api('/reveal', { path: r.zip }) }, t('btn.openFolder')),
        el('button', { class: 'btn primary', onclick: closeModal }, t('btn.ok'))]);
  }
  render();
}

// -------------------------------------------------------------------------------------------------
// Onglets
// -------------------------------------------------------------------------------------------------

/** Affiche l'onglet S.tab (les boutons propres a la traduction sont caches dans l'onglet Modlist). */
function applyTab() {
  const ml = S.tab === 'modlist';
  document.body.classList.toggle('tab-modlist', ml);
  $('tabTranslate').classList.toggle('on', !ml);
  $('tabModlist').classList.toggle('on', ml);
  $('tabTranslate').setAttribute('aria-selected', String(!ml));
  $('tabModlist').setAttribute('aria-selected', String(ml));
  renderHeader();
}

async function switchTab(tab) {
  if (S.tab === tab) return;
  if (tab === 'modlist') await flush();
  S.tab = tab;
  applyTab();
  // La liste de MO2 est relue a chaque passage sur l'onglet : seuls les mods qui ont change sont reanalyses
  if (tab === 'modlist') mlLoad({});
}

// -------------------------------------------------------------------------------------------------
// Onglet Modlist : la liste de MO2, meme ordre et memes separateurs.
// Vert = un mod « Nom - FR » est dans MO2 ; orange = du texte a traduire ; sans couleur = rien a traduire.
// -------------------------------------------------------------------------------------------------

const ML = {
  data: null,
  revision: -1,
  search: '',
  pollTimer: null,
  prefsTimer: null,
  prefsDirty: false,
  loading: false,
  // Pas de redessin pendant un clic (sinon le clic ou le double-clic se perd) ni menu ouvert
  pointerDown: false,
  pointerAt: 0,
  renderPending: false,
  profileKey: '',
  sepMenuKey: '',
  prefs: { profile: '', hidden: [], collapsed: [], hideDisabled: false, hiddenStatuses: [] },
};

/** Statuts des mods, dans l'ordre des filtres. color : null = sans couleur. */
const ML_STATUS = {
  todo: { label: 'ml.status.todo', tip: 'ml.status.todo.tip', color: 'var(--ml-todo)' },
  translated: { label: 'ml.status.translated', tip: 'ml.status.translated.tip', color: 'var(--ml-translated)' },
  none: { label: 'ml.status.none', tip: 'ml.status.none.tip', color: null },
  trad: { label: 'ml.status.trad', tip: 'ml.status.trad.tip', color: 'var(--ml-trad)' },
  error: { label: 'ml.status.error', tip: 'ml.status.error.tip', color: 'var(--ml-error)' },
  pending: { label: 'ml.status.pending', tip: null, color: 'var(--accent-2)' },
};

/** Relit la liste de MO2 (profile : changer de profil, force : tout reanalyser). */
async function mlLoad(body) {
  clearTimeout(ML.pollTimer);
  if (!S.state.settings.mo2ModsDir) {
    ML.data = null;
    mlRender();
    return;
  }
  ML.loading = true;
  if (!ML.data) mlRender();
  try {
    ML.data = await api('/modlist', body);
    ML.revision = -1;
  } catch (e) {
    reportError(e, 'modlist');
    ML.data = { error: e.message, rows: [], profiles: [], scanning: { active: false } };
  } finally {
    ML.loading = false;
  }
  mlRender();
  mlSchedulePoll();
}

function mlSchedulePoll() {
  clearTimeout(ML.pollTimer);
  if (ML.data && ML.data.scanning && ML.data.scanning.active && S.tab === 'modlist') ML.pollTimer = setTimeout(mlPoll, 700);
  else if (ML.renderPending) setTimeout(mlRenderWhenIdle, 300);
}

/** L'utilisateur est en train de cliquer, ou un menu est ouvert : un redessin lui volerait son clic. */
function mlInteracting() {
  return ML.pointerDown || Date.now() - ML.pointerAt < 500 ||
    !$('mlSepMenu').classList.contains('hidden') || document.activeElement === $('mlProfile');
}

function mlRenderWhenIdle() {
  if (!ML.renderPending || S.tab !== 'modlist') return;
  if (mlInteracting()) {
    setTimeout(mlRenderWhenIdle, 300);
    return;
  }
  ML.renderPending = false;
  mlRender();
}

async function mlPoll() {
  try {
    const d = await api('/modlist');
    if (!ML.data || d.revision !== ML.data.revision) ML.renderPending = true;
    ML.data = d;
    if (ML.renderPending && !mlInteracting()) {
      ML.renderPending = false;
      mlRender();
    }
  } catch (e) {
    reportError(e, 'modlist');
  }
  mlSchedulePoll();
}

function mlSavePrefs() {
  clearTimeout(ML.prefsTimer);
  ML.prefsDirty = true;
  ML.prefsTimer = setTimeout(() => {
    ML.prefsDirty = false;
    api('/modlist/prefs', ML.prefs).catch((e) => {
      reportError(e, 'modlist');
      toast(e.message, 'error');
    });
  }, 400);
}

const mlList = (name) => ML.prefs[name] || (ML.prefs[name] = []);
const mlHas = (name, value) => mlList(name).includes(value);
function mlToggle(name, value, on) {
  const list = mlList(name);
  const i = list.indexOf(value);
  if (on && i < 0) list.push(value);
  if (!on && i >= 0) list.splice(i, 1);
  mlSavePrefs();
}

/** Lignes d'un mod affichees (filtres de statut, mods desactives, recherche). */
function mlVisible(r) {
  if (mlHas('hiddenStatuses', r.status)) return false;
  if (ML.prefs.hideDisabled && !r.enabled) return false;
  const q = ML.search.trim().toLowerCase();
  return !q || r.display.toLowerCase().includes(q);
}

const mlFiltering = () => !!ML.search.trim() || ML.prefs.hideDisabled || mlList('hiddenStatuses').length > 0;

function mlRender() {
  const list = $('mlList');
  const scroll = list.scrollTop;
  const d = ML.data;
  const configured = !!S.state.settings.mo2ModsDir;
  $('mlToolbar').classList.toggle('hidden', !configured || !d || !!d.error);
  $('mlFilters').classList.toggle('hidden', !configured || !d || !!d.error);
  $('mlSummary').replaceChildren();
  list.replaceChildren();

  if (!configured) {
    list.append(el('div', { class: 'ml-card' },
      el('h2', {}, t('ml.setup.title')),
      el('p', {}, t('ml.setup.text')),
      el('p', {}, t('ml.setup.legend', langSuffix(targetLang()))),
      el('div', { class: 'buttons' }, el('button', { class: 'btn primary', onclick: openSettings }, t('settings.title')))));
    return;
  }
  if (!d) {
    list.append(el('div', { class: 'empty' }, t('busy.default')));
    return;
  }
  if (d.error) {
    list.append(el('div', { class: 'ml-card' },
      el('h2', {}, t('ml.error.title')),
      el('div', { class: 'mono' }, d.error),
      el('div', { class: 'buttons' },
        el('button', { class: 'btn primary', onclick: openSettings }, t('settings.title')),
        el('button', { class: 'btn', onclick: () => mlLoad({}) }, t('ml.retry')))));
    return;
  }

  mlRenderToolbar(d);
  mlRenderSummary(d);

  const rows = d.rows;
  const filtering = mlFiltering();
  const hidden = new Set(mlList('hidden'));
  const collapsed = new Set(mlList('collapsed'));
  const children = new Map();
  for (const r of rows) {
    if (r.kind === 'sep') continue;
    if (!children.has(r.group)) children.set(r.group, []);
    children.get(r.group).push(r);
  }

  let shown = 0;
  // Mods au-dessus du premier separateur : pas d'en-tete, comme dans MO2
  for (const r of (children.get(-1) || []).filter(mlVisible)) {
    list.append(mlRowEl(r));
    shown++;
  }
  rows.forEach((sep, index) => {
    if (sep.kind !== 'sep' || hidden.has(sep.name)) return;
    const kids = children.get(index) || [];
    const visible = kids.filter(mlVisible);
    if (filtering && visible.length === 0) return;
    const isCollapsed = collapsed.has(sep.name) && !ML.search.trim();
    const group = el('div', { class: 'ml-group' }, mlSepEl(sep, kids, isCollapsed));
    if (!isCollapsed) for (const r of visible) group.append(mlRowEl(r));
    list.append(group);
    shown += visible.length;
  });
  if (shown === 0 && rows.some((r) => r.kind !== 'sep')) list.append(el('div', { class: 'empty' }, t('ml.empty')));
  list.scrollTop = scroll;
}

function mlRenderToolbar(d) {
  // Profils
  const select = $('mlProfile');
  const profileKey = JSON.stringify([d.profiles, d.profile, d.activeProfile, UI.code]);
  if (profileKey !== ML.profileKey) {
    ML.profileKey = profileKey;
    select.replaceChildren(...d.profiles.map((p) => el('option', { value: p, selected: p === d.profile },
      p === d.activeProfile ? t('ml.profile.active', p) : p)));
  }
  select.parentElement.classList.toggle('hidden', d.profiles.length < 2);

  // Filtres de statut, avec les compteurs
  const mods = d.rows.filter((r) => r.kind !== 'sep');
  const chips = $('mlChips');
  chips.replaceChildren();
  const suffix = d.suffix || langSuffix(targetLang());
  for (const [key, info] of Object.entries(ML_STATUS)) {
    const count = mods.filter((r) => r.status === key).length;
    if (count === 0 && (key === 'error' || key === 'pending' || key === 'trad')) continue;
    const on = !mlHas('hiddenStatuses', key);
    chips.append(el('button', {
      class: `chip ${on ? 'on' : ''}`,
      title: info.tip ? t(info.tip, suffix) : '',
      onclick: () => {
        mlToggle('hiddenStatuses', key, on);
        mlRender();
      },
    }, el('span', { class: `dot ${info.color ? '' : 'hollow'}`, style: info.color ? `background:${info.color}` : null }), `${t(info.label)} (${count})`));
  }
  $('mlHideDisabled').checked = !!ML.prefs.hideDisabled;

  // Bouton replier / deplier
  const seps = d.rows.filter((r) => r.kind === 'sep');
  const allCollapsed = seps.length > 0 && seps.every((r) => mlHas('collapsed', r.name));
  const btn = $('btnMlCollapse');
  btn.textContent = allCollapsed ? t('ml.expandAll') : t('ml.collapseAll');
  btn.dataset.expand = allCollapsed ? '1' : '';
  btn.disabled = seps.length === 0;
  mlRenderSepMenu(d);
}

/** Menu « Separateurs » : une case par separateur, decochee = cache (avec tous ses mods). */
function mlRenderSepMenu(d) {
  const menu = $('mlSepMenu');
  const seps = d.rows.filter((r) => r.kind === 'sep');
  const key = JSON.stringify([seps.map((r) => [r.name, r.color]), UI.code]);
  if (key === ML.sepMenuKey) {
    for (const cb of menu.querySelectorAll('input[data-name]')) cb.checked = !mlHas('hidden', cb.dataset.name);
    return;
  }
  ML.sepMenuKey = key;
  menu.replaceChildren();
  if (seps.length === 0) {
    menu.append(el('div', { class: 'none' }, t('ml.separators.none')));
    return;
  }
  const setAll = (hide) => {
    ML.prefs.hidden = hide ? seps.map((r) => r.name) : [];
    mlSavePrefs();
    mlRender();
  };
  menu.append(el('div', { class: 'ml-seps-tools' },
    el('button', { class: 'btn small', onclick: () => setAll(false) }, t('ml.separators.showAll')),
    el('button', { class: 'btn small', onclick: () => setAll(true) }, t('ml.separators.hideAll'))));
  for (const sep of seps) {
    const cb = el('input', { type: 'checkbox', checked: !mlHas('hidden', sep.name), 'data-name': sep.name });
    cb.addEventListener('change', () => {
      mlToggle('hidden', sep.name, !cb.checked);
      mlRender();
    });
    menu.append(el('label', {}, cb,
      el('span', { class: 'swatch', style: `background:${sep.color || 'var(--line)'}` }), sep.display));
  }
}

function mlRenderSummary(d) {
  const box = $('mlSummary');
  const mods = d.rows.filter((r) => r.kind === 'mod');
  const translated = mods.filter((r) => r.status === 'translated').length;
  const withText = translated + mods.filter((r) => r.status === 'todo').length;
  if (withText > 0) {
    box.append(el('span', {}, t('ml.progress', translated, withText)),
      el('div', { class: 'ml-bar' }, el('div', { style: `width:${Math.round((translated * 100) / withText)}%` })));
  } else if (!d.scanning.active) {
    box.append(el('span', {}, t('ml.progress.none')));
  }
  const hiddenCount = d.rows.filter((r) => r.kind === 'sep' && mlHas('hidden', r.name)).length;
  if (hiddenCount) {
    box.append(el('span', {}, t('ml.hiddenSeps', hiddenCount), ' · ',
      el('a', { onclick: () => { ML.prefs.hidden = []; mlSavePrefs(); mlRender(); } }, t('ml.separators.showAll'))));
  }
  if (d.noDll) box.append(el('span', { class: 'warn', title: t('ml.noDll') }, '⚠ ' + t('statusbar.dllMissing')));
  if (d.scanning.active) {
    const sc = d.scanning;
    box.append(el('span', { title: sc.current || '' }, t('ml.scanning', sc.done, sc.total)),
      el('div', { class: 'ml-bar scan' }, el('div', { style: `width:${sc.total ? Math.round((sc.done * 100) / sc.total) : 0}%` })));
  } else {
    box.append(el('span', { class: 'ok' }, t('ml.upToDate')),
      el('a', { title: t('ml.rescan.tip'), onclick: () => mlLoad({ force: true }) }, t('ml.rescan')));
  }
}

function mlSepEl(sep, kids, isCollapsed) {
  const mods = kids.filter((r) => r.kind === 'mod');
  const todo = mods.filter((r) => r.status === 'todo').length;
  const done = mods.filter((r) => r.status === 'translated').length;
  const node = el('div', {
    class: `ml-sep ${isCollapsed ? 'collapsed' : ''}`,
    style: sep.color ? `--sep-color:${sep.color}` : null,
    title: t('ml.sep.toggle'),
    onclick: () => {
      mlToggle('collapsed', sep.name, !mlHas('collapsed', sep.name));
      mlRender();
    },
  },
  el('span', { class: 'chev' }, '▼'),
  el('span', { class: 'sname' }, sep.display),
  el('span', { class: 'scount' },
    el('span', {}, t('ml.sep.count', kids.length)),
    todo ? el('span', { class: 't' }, t('ml.sep.todo', todo)) : null,
    done ? el('span', { class: 'g' }, t('ml.sep.translated', done)) : null),
  el('button', {
    class: 'hide',
    title: t('ml.sep.hide'),
    onclick: (e) => {
      e.stopPropagation();
      mlToggle('hidden', sep.name, true);
      mlRender();
    },
  }, '✕'));
  return node;
}

/** Texte sous le nom du mod : ce qui reste a faire, ou par quoi il est traduit. */
function mlInfo(r) {
  switch (r.status) {
    case 'todo': {
      const parts = [];
      if (r.todo) parts.push(el('b', {}, t('ml.info.todo', r.todo)));
      if (r.check) parts.push(t('ml.info.check', r.check));
      if (r.saved) parts.push(t('ml.info.started', r.saved, r.todo + r.check));
      return parts.flatMap((x, i) => (i ? [' · ', x] : [x]));
    }
    case 'translated': return [el('b', {}, t('ml.info.translated', r.translation))];
    case 'none': return [r.done ? t('ml.info.authorDone', r.done) : t('ml.info.none')];
    case 'trad': return [r.originalFound ? t('ml.info.trad', r.original) : t('ml.info.tradOrphan')];
    case 'error': return [t('ml.info.error', r.error || '')];
    default: return [t('ml.info.pending')];
  }
}

function mlRowEl(r) {
  const tags = [];
  if (!r.enabled) tags.push(el('span', { class: 'ml-tag' }, r.notInProfile ? t('ml.tag.notInProfile') : t('ml.tag.disabled')));
  if (r.translation && !r.translationEnabled) {
    tags.push(el('span', { class: 'ml-tag warn', title: t('ml.tag.tradDisabled.tip', r.translation) }, t('ml.tag.tradDisabled')));
  }
  if (r.built && r.status !== 'translated') {
    const name = `${r.name} - ${ML.data.suffix}`;
    tags.push(el('span', { class: 'ml-tag info', title: t('ml.tag.built.tip', r.built, name) }, t('ml.tag.built')));
  }
  // Bouton : seulement si le mod a des lignes (sinon l'ouverture echouerait sur « aucun texte »)
  const hasText = r.todo + r.check + r.done > 0;
  const canOpen = r.kind === 'mod' && r.path && hasText && r.status !== 'error';
  const action = canOpen
    ? el('button', {
      class: `btn small ${r.status === 'todo' ? 'go' : ''}`,
      onclick: (e) => {
        e.stopPropagation();
        mlOpen(r);
      },
    }, r.status === 'todo' ? t('ml.btn.translate') : t('ml.btn.open'))
    : el('span', { class: 'act' });
  const info = mlInfo(r);
  return el('div', {
    class: `ml-row st-${r.status} ${r.enabled ? '' : 'off'}`,
    title: r.path || '',
    ondblclick: canOpen ? () => mlOpen(r) : null,
  },
  el('div', { class: 'mname' }, r.display),
  el('div', { class: 'minfo', title: r.status === 'error' ? r.error : null }, ...info),
  el('div', { class: 'ml-tags' }, ...tags),
  action);
}

async function mlOpen(r) {
  await openMod(r.path);
}

// -------------------------------------------------------------------------------------------------
// Demarrage
// -------------------------------------------------------------------------------------------------

function bind() {
  $('btnOpen').addEventListener('click', () => openMod());
  $('btnOpenBig').addEventListener('click', () => openMod());
  $('btnImportTrad').addEventListener('click', importTranslation);
  $('btnSave').addEventListener('click', save);
  $('btnSettings').addEventListener('click', openSettings);
  $('btnBuild').addEventListener('click', buildTranslation);
  $('btnTsv').addEventListener('click', (e) => {
    e.stopPropagation();
    $('tsvMenu').classList.toggle('hidden');
  });
  document.addEventListener('click', () => {
    $('tsvMenu').classList.add('hidden');
    $('mlSepMenu').classList.add('hidden');
  });

  // Onglets et onglet Modlist
  $('tabTranslate').addEventListener('click', () => switchTab('translate'));
  $('tabModlist').addEventListener('click', () => switchTab('modlist'));
  $('btnMlSeps').addEventListener('click', (e) => {
    e.stopPropagation();
    $('mlSepMenu').classList.toggle('hidden');
  });
  $('mlSepMenu').addEventListener('click', (e) => e.stopPropagation());
  $('modlistView').addEventListener('pointerdown', () => {
    ML.pointerDown = true;
    ML.pointerAt = Date.now();
  });
  document.addEventListener('pointerup', () => {
    ML.pointerDown = false;
    ML.pointerAt = Date.now();
  });
  $('btnMlRefresh').addEventListener('click', () => mlLoad({}));
  $('btnMlCollapse').addEventListener('click', () => {
    const expand = $('btnMlCollapse').dataset.expand === '1';
    ML.prefs.collapsed = expand ? [] : ML.data.rows.filter((r) => r.kind === 'sep').map((r) => r.name);
    mlSavePrefs();
    mlRender();
  });
  $('mlProfile').addEventListener('change', () => {
    ML.prefs.profile = $('mlProfile').value;
    mlLoad({ profile: $('mlProfile').value });
  });
  $('mlHideDisabled').addEventListener('change', () => {
    ML.prefs.hideDisabled = $('mlHideDisabled').checked;
    mlSavePrefs();
    mlRender();
  });
  let mlSearchTimer = null;
  $('mlSearch').addEventListener('input', () => {
    clearTimeout(mlSearchTimer);
    mlSearchTimer = setTimeout(() => {
      ML.search = $('mlSearch').value;
      mlRender();
    }, 150);
  });
  $('btnExportTsv').addEventListener('click', exportTsv);
  $('btnImportTsv').addEventListener('click', importTsv);

  let searchTimer = null;
  $('search').addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      S.search = $('search').value;
      S.page = 0;
      applyFilters();
    }, 200);
  });
  $('onlyEmpty').addEventListener('change', () => {
    S.onlyEmpty = $('onlyEmpty').checked;
    S.page = 0;
    applyFilters();
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !$('modal').classList.contains('hidden')) closeModal();
    if (e.ctrlKey && e.key.toLowerCase() === 's') {
      e.preventDefault();
      if (S.state && S.state.project && S.tab === 'translate') save();
    }
    if (e.ctrlKey && e.key.toLowerCase() === 'f') {
      const box = S.tab === 'modlist' ? $('mlSearch') : (S.state && S.state.project ? $('search') : null);
      if (box && box.offsetParent !== null) {
        e.preventDefault();
        box.focus();
        box.select();
      }
    }
  });

  window.addEventListener('pagehide', flushOnExit);
  setInterval(() => { api('/ping', {}).catch(() => {}); }, 5000);
  api('/ping', {}).catch(() => {});
}

async function init() {
  bind();
  try {
    await loadLanguage();
  } catch (e) {
    // Pas de textes : la page garde ses textes anglais, le message d'erreur vient juste apres
    reportError(e, 'langue');
  }
  document.body.classList.remove('i18n-loading');
  try {
    await refreshState();
    if (S.state.settings.modlist) ML.prefs = S.state.settings.modlist;
    applyTab();
    if (S.state.project) await loadUnits();
    renderDetail(null);
  } catch (e) {
    reportError(e, 'demarrage');
    toast(t('error.unreachable', e.message), 'error');
  }
}

init();
