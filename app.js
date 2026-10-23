(function () {
  'use strict';

  const API = 'https://www.googleapis.com/youtube/v3/';
  const SETTINGS_KEY = 'ytf.settings';
  const CACHE_KEY = 'ytf.cache';
  const CACHE_TTL = 30 * 60 * 1000;

  // Коды кнопки «Назад» на пультах разных ТВ-платформ (VIDAA, webOS, Tizen) и Esc.
  const BACK_KEYS = [8, 27, 461, 10009];
  const ARROWS = { 37: 'left', 38: 'up', 39: 'right', 40: 'down' };
  const MEDIA = { 415: 'play', 19: 'pause', 179: 'toggle', 412: 'back10', 417: 'fwd10', 413: 'close' };

  const $ = (sel) => document.querySelector(sel);
  const config = window.APP_CONFIG || {};

  const state = {
    settings: loadSettings(),
    channels: [],
    videos: [],
    activeChannel: null,
    query: '',
    overlay: null,
    pinPassed: false,
  };

  let ytPlayer = null;
  let ytReady = null;
  let lastFocus = null;

  // ---------- Настройки ----------

  function loadSettings() {
    let saved = {};
    try {
      saved = JSON.parse(localStorage.getItem(SETTINGS_KEY)) || {};
    } catch (e) { /* хранилище недоступно */ }
    return {
      apiKey: saved.apiKey || config.apiKey || '',
      channels: saved.channels || config.channels || [],
      pin: saved.pin !== undefined ? saved.pin : (config.pin || ''),
      videosPerChannel: Math.min(config.videosPerChannel || 30, 50),
    };
  }

  function saveSettings(values) {
    try {
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(values));
      localStorage.removeItem(CACHE_KEY);
    } catch (e) { /* хранилище недоступно */ }
    state.settings = loadSettings();
  }

  function resetSettings() {
    try {
      localStorage.removeItem(SETTINGS_KEY);
      localStorage.removeItem(CACHE_KEY);
    } catch (e) { /* хранилище недоступно */ }
    state.settings = loadSettings();
  }

  // ---------- YouTube Data API ----------

  async function api(method, params) {
    const url = new URL(API + method);
    Object.keys(params).forEach((k) => url.searchParams.set(k, params[k]));
    url.searchParams.set('key', state.settings.apiKey);
    const res = await fetch(url.toString());
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      throw new Error((data.error && data.error.message) || ('HTTP ' + res.status));
    }
    return data;
  }

  function parseChannelRef(ref) {
    ref = ref.trim();
    const m = ref.match(/youtube\.com\/(?:channel\/(UC[\w-]{22})|(@[^/?#\s]+))/);
    if (m) return m[1] || decodeURIComponent(m[2]);
    return ref;
  }

  async function resolveChannel(ref) {
    const id = parseChannelRef(ref);
    const params = { part: 'snippet,contentDetails' };
    if (/^UC[\w-]{22}$/.test(id)) params.id = id;
    else params.forHandle = id.charAt(0) === '@' ? id : '@' + id;

    const data = await api('channels', params);
    const ch = data.items && data.items[0];
    if (!ch) throw new Error('канал не найден');
    return {
      id: ch.id,
      title: ch.snippet.title,
      thumb: ch.snippet.thumbnails && ch.snippet.thumbnails.default && ch.snippet.thumbnails.default.url,
      uploads: ch.contentDetails.relatedPlaylists.uploads,
    };
  }

  async function loadChannelVideos(channel) {
    const data = await api('playlistItems', {
      part: 'snippet',
      playlistId: channel.uploads,
      maxResults: state.settings.videosPerChannel,
    });
    return (data.items || [])
      // Удалённые и приватные видео приходят без превью.
      .filter((it) => it.snippet.resourceId && it.snippet.resourceId.videoId && it.snippet.thumbnails &&
        Object.keys(it.snippet.thumbnails).length)
      .map((it) => {
        const t = it.snippet.thumbnails;
        return {
          id: it.snippet.resourceId.videoId,
          title: it.snippet.title,
          channelId: channel.id,
          channelTitle: channel.title,
          published: it.snippet.publishedAt,
          thumb: (t.medium || t.high || t.default).url,
        };
      });
  }

  async function loadAll(force) {
    const s = state.settings;
    if (!s.apiKey || !s.channels.length) {
      setStatus('Укажите ключ API и разрешённые каналы в настройках (⚙).');
      state.channels = [];
      state.videos = [];
      render();
      return;
    }

    const signature = JSON.stringify([s.apiKey, s.channels, s.videosPerChannel]);
    if (!force) {
      const cached = readCache(signature);
      if (cached) {
        state.channels = cached.channels;
        state.videos = cached.videos;
        setStatus('');
        render();
        return;
      }
    }

    setStatus('Загрузка…');
    const errors = [];
    const results = await Promise.all(s.channels.map(async (ref) => {
      try {
        const channel = await resolveChannel(ref);
        return { channel, videos: await loadChannelVideos(channel) };
      } catch (e) {
        errors.push(ref + ': ' + e.message);
        return null;
      }
    }));

    const ok = results.filter(Boolean);
    state.channels = ok.map((r) => r.channel);
    state.videos = ok.reduce((all, r) => all.concat(r.videos), [])
      .sort((a, b) => (a.published < b.published ? 1 : -1));
    if (state.activeChannel && !state.channels.some((c) => c.id === state.activeChannel)) {
      state.activeChannel = null;
    }

    if (ok.length) writeCache(signature);
    setStatus(errors.length ? 'Не удалось загрузить: ' + errors.join('; ') : '');
    render();
  }

  function readCache(signature) {
    try {
      const c = JSON.parse(localStorage.getItem(CACHE_KEY));
      if (c && c.signature === signature && Date.now() - c.time < CACHE_TTL) return c;
    } catch (e) { /* нет кэша */ }
    return null;
  }

  function writeCache(signature) {
    try {
      localStorage.setItem(CACHE_KEY, JSON.stringify({
        signature, time: Date.now(), channels: state.channels, videos: state.videos,
      }));
    } catch (e) { /* хранилище недоступно */ }
  }

  // ---------- Отрисовка ----------

  function setStatus(text) {
    $('#status').textContent = text;
  }

  function render() {
    renderChips();
    renderGrid();
  }

  function renderChips() {
    const chips = $('#chips');
    chips.textContent = '';
    if (!state.channels.length) return;

    chips.appendChild(chip('Все', null, null));
    state.channels.forEach((c) => chips.appendChild(chip(c.title, c.id, c.thumb)));
  }

  function chip(label, channelId, thumb) {
    const b = document.createElement('button');
    b.className = 'chip' + (state.activeChannel === channelId ? ' active' : '');
    b.setAttribute('data-nav', '');
    if (thumb) {
      const img = document.createElement('img');
      img.src = thumb;
      img.alt = '';
      b.appendChild(img);
    }
    b.appendChild(document.createTextNode(label));
    b.addEventListener('click', () => {
      state.activeChannel = channelId;
      render();
      focusChip(channelId);
    });
    return b;
  }

  function focusChip(channelId) {
    const idx = channelId ? state.channels.findIndex((c) => c.id === channelId) + 1 : 0;
    const el = $('#chips').children[idx];
    if (el) el.focus();
  }

  function visibleVideos() {
    const q = state.query.trim().toLowerCase();
    return state.videos.filter((v) =>
      (!state.activeChannel || v.channelId === state.activeChannel) &&
      (!q || v.title.toLowerCase().indexOf(q) !== -1));
  }

  function renderGrid() {
    const grid = $('#grid');
    grid.textContent = '';
    const list = visibleVideos();
    if (!list.length && state.videos.length) {
      setStatus('Ничего не найдено.');
    } else if ($('#status').textContent === 'Ничего не найдено.') {
      setStatus('');
    }

    const fmt = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'short', year: 'numeric' });
    list.forEach((v) => {
      const card = document.createElement('button');
      card.className = 'card';
      card.setAttribute('data-nav', '');

      const img = document.createElement('img');
      img.src = v.thumb;
      img.alt = '';
      img.loading = 'lazy';

      const title = document.createElement('p');
      title.className = 'title';
      title.textContent = v.title;

      const meta = document.createElement('p');
      meta.className = 'meta';
      meta.textContent = v.channelTitle + ' · ' + fmt.format(new Date(v.published));

      card.append(img, title, meta);
      card.addEventListener('click', () => playVideo(v));
      grid.appendChild(card);
    });
  }

  // ---------- Оверлеи и история (кнопка «Назад») ----------

  function openOverlay(name) {
    if (state.overlay) hideOverlay();
    state.overlay = name;
    $('#' + name + '-overlay').hidden = false;
    history.pushState({ overlay: name }, '');
  }

  function closeOverlay() {
    if (!state.overlay) return false;
    if (history.state && history.state.overlay) history.back(); // popstate спрячет оверлей
    else hideOverlay();
    return true;
  }

  function hideOverlay() {
    const name = state.overlay;
    if (!name) return;
    state.overlay = null;
    $('#' + name + '-overlay').hidden = true;
    if (name === 'player' && ytPlayer && ytPlayer.stopVideo) ytPlayer.stopVideo();
    if (lastFocus && document.contains(lastFocus)) lastFocus.focus();
  }

  window.addEventListener('popstate', hideOverlay);

  // ---------- Плеер ----------

  function loadYT() {
    if (!ytReady) {
      ytReady = new Promise((resolve) => {
        window.onYouTubeIframeAPIReady = resolve;
        const s = document.createElement('script');
        s.src = 'https://www.youtube.com/iframe_api';
        document.head.appendChild(s);
      });
    }
    return ytReady;
  }

  function playVideo(v) {
    lastFocus = document.activeElement;
    openOverlay('player');
    $('#player-title').textContent = v.title;
    $('#player-error').hidden = true;
    $('#player-overlay [data-action="toggle"]').focus();

    loadYT().then(() => {
      if (ytPlayer) {
        ytPlayer.loadVideoById(v.id);
        return;
      }
      ytPlayer = new window.YT.Player('player', {
        host: 'https://www.youtube-nocookie.com',
        videoId: v.id,
        playerVars: { autoplay: 1, rel: 0, iv_load_policy: 3, playsinline: 1, fs: 0, disablekb: 1 },
        events: {
          onReady: (e) => {
            // Фокус остаётся на нашей панели, чтобы пульт управлял приложением.
            e.target.getIframe().setAttribute('tabindex', '-1');
          },
          onStateChange: (e) => {
            // По окончании закрываем плеер, чтобы не показывать рекомендации YouTube.
            if (e.data === window.YT.PlayerState.ENDED) closeOverlay();
          },
          onError: (e) => {
            const el = $('#player-error');
            el.textContent = (e.data === 101 || e.data === 150)
              ? 'Автор запретил воспроизведение этого видео вне YouTube.'
              : 'Не удалось воспроизвести видео (код ' + e.data + ').';
            el.hidden = false;
          },
        },
      });
    });
  }

  function playerAction(action) {
    if (action === 'close') { closeOverlay(); return; }
    if (!ytPlayer || !ytPlayer.getPlayerState) return;
    const playing = ytPlayer.getPlayerState() === window.YT.PlayerState.PLAYING;
    if (action === 'toggle') playing ? ytPlayer.pauseVideo() : ytPlayer.playVideo();
    else if (action === 'play') ytPlayer.playVideo();
    else if (action === 'pause') ytPlayer.pauseVideo();
    else if (action === 'back10' || action === 'fwd10') {
      const delta = action === 'back10' ? -10 : 10;
      ytPlayer.seekTo(Math.max(0, ytPlayer.getCurrentTime() + delta), true);
    }
  }

  $('#player-overlay').addEventListener('click', (e) => {
    const btn = e.target.closest('[data-action]');
    if (btn) playerAction(btn.getAttribute('data-action'));
  });

  // ---------- Настройки (UI) ----------

  function openSettings() {
    lastFocus = document.activeElement;
    openOverlay('settings');
    const needPin = state.settings.pin && !state.pinPassed;
    $('#pin-gate').hidden = !needPin;
    $('#settings-form').hidden = needPin;
    $('#pin-error').hidden = true;
    if (needPin) {
      $('#pin-input').value = '';
      $('#pin-input').focus();
    } else {
      fillSettingsForm();
    }
  }

  function fillSettingsForm() {
    $('#set-key').value = state.settings.apiKey;
    $('#set-channels').value = state.settings.channels.join('\n');
    $('#set-pin').value = state.settings.pin;
    $('#pin-gate').hidden = true;
    $('#settings-form').hidden = false;
    $('#set-key').focus();
  }

  $('#pin-gate').addEventListener('submit', (e) => {
    e.preventDefault();
    if ($('#pin-input').value === String(state.settings.pin)) {
      state.pinPassed = true;
      fillSettingsForm();
    } else {
      $('#pin-error').hidden = false;
    }
  });

  $('#settings-form').addEventListener('submit', (e) => {
    e.preventDefault();
    saveSettings({
      apiKey: $('#set-key').value.trim(),
      channels: $('#set-channels').value.split('\n').map((s) => s.trim()).filter(Boolean),
      pin: $('#set-pin').value.trim(),
    });
    closeOverlay();
    loadAll(true);
  });

  $('#reset-btn').addEventListener('click', () => {
    resetSettings();
    closeOverlay();
    loadAll(true);
  });

  $('#settings-close').addEventListener('click', closeOverlay);
  $('#settings-btn').addEventListener('click', openSettings);
  $('#refresh-btn').addEventListener('click', () => loadAll(true));

  $('#search').addEventListener('input', (e) => {
    state.query = e.target.value;
    renderGrid();
  });

  // ---------- Навигация пультом ----------

  function isTextField(el) {
    return el && (el.tagName === 'TEXTAREA' || (el.tagName === 'INPUT' && el.type !== 'button'));
  }

  function navItems() {
    const root = state.overlay ? $('#' + state.overlay + '-overlay') : document;
    return Array.prototype.filter.call(root.querySelectorAll('[data-nav]'),
      (el) => el.offsetParent !== null && !(state.overlay === null && el.closest('.overlay')));
  }

  function move(dir) {
    const items = navItems();
    const cur = document.activeElement;
    if (items.indexOf(cur) === -1) {
      if (items[0]) items[0].focus();
      return;
    }
    const r = cur.getBoundingClientRect();
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    let best = null;
    let bestScore = Infinity;

    items.forEach((el) => {
      if (el === cur) return;
      const b = el.getBoundingClientRect();
      const dx = b.left + b.width / 2 - cx;
      const dy = b.top + b.height / 2 - cy;
      let main;
      let cross;
      if (dir === 'left') { main = -dx; cross = dy; }
      else if (dir === 'right') { main = dx; cross = dy; }
      else if (dir === 'up') { main = -dy; cross = dx; }
      else { main = dy; cross = dx; }
      if (main <= 1) return;
      const score = main + Math.abs(cross) * (dir === 'left' || dir === 'right' ? 4 : 2);
      if (score < bestScore) { bestScore = score; best = el; }
    });

    if (best) {
      best.focus();
      best.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
  }

  document.addEventListener('keydown', (e) => {
    const k = e.keyCode;
    const active = document.activeElement;

    if (BACK_KEYS.indexOf(k) !== -1) {
      if (k === 8 && isTextField(active)) return; // обычный Backspace в поле ввода
      if (closeOverlay()) e.preventDefault();
      return;
    }

    if (state.overlay === 'player' && MEDIA[k]) {
      e.preventDefault();
      playerAction(MEDIA[k]);
      return;
    }

    const dir = ARROWS[k];
    if (!dir) return;
    if (isTextField(active)) {
      if (dir === 'left' || dir === 'right') return;
      if (active.tagName === 'TEXTAREA') {
        const v = active.value;
        const atStart = active.selectionStart === 0;
        const atEnd = active.selectionEnd === v.length;
        if ((dir === 'up' && !atStart) || (dir === 'down' && !atEnd)) return;
      }
    }
    e.preventDefault();
    move(dir);
  });

  // ---------- Старт ----------

  loadAll(false).then(() => {
    const first = $('#grid .card') || $('#settings-btn');
    if (first) first.focus();
  });
})();
