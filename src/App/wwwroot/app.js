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
  if (!r.ok) throw new Error((data && data.error) || t('error.http', r.status));
  return data;
}

async function busy(text, fn) {
  $('busyText').textContent = text;
  $('busy').classList.remove('hidden');
  try {
    return await fn();
  } catch (e) {
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
  $('welcome').classList.toggle('hidden', !!p);
  $('workspace').classList.toggle('hidden', !p);

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
    if (!path) return;
  }
  await flush();
  const st = await busy(t('open.busy'), () => api('/open', { path }));
  if (!st) return;
  S.state = st;
  renderHeader();
  await loadUnits();
  const p = st.project;
  toast(t('open.done', p.name, S.units.filter((u) => u.status !== 'code').length, p.files.length), 'ok');
  if (p.restored > 0) toast(t('open.restored', p.restored), 'ok');
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
  document.addEventListener('click', () => $('tsvMenu').classList.add('hidden'));
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
      if (S.state && S.state.project) save();
    }
    if (e.ctrlKey && e.key.toLowerCase() === 'f' && S.state && S.state.project) {
      e.preventDefault();
      $('search').focus();
      $('search').select();
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
  } catch {
    // pas de textes : la page garde ses textes anglais, le message d'erreur vient juste apres
  }
  document.body.classList.remove('i18n-loading');
  try {
    await refreshState();
    if (S.state.project) await loadUnits();
    renderDetail(null);
  } catch (e) {
    toast(t('error.unreachable', e.message), 'error');
  }
}

init();
