const allowedStatuses = new Set(['enabled', 'disabled', 'available', 'error']);

const text = (value) => typeof value === 'string' || typeof value === 'number' ? String(value) : '—';

const appendDefinition = (list, label, value) => {
    const term = document.createElement('dt');
    term.textContent = label;
    const definition = document.createElement('dd');
    definition.textContent = text(value);
    list.append(term, definition);
};

const safeUrl = (value) => {
    try {
        const url = new URL(value, window.location.origin);
        return url.protocol === 'https:' ? url.href : null;
    } catch (_) {
        return null;
    }
};

const visibilityLabel = (value) => {
    if (value === 'public') return 'Público (listado)';
    if (value === 'private') return 'Privado (no listado)';
    return '—';
};

const setServerText = (panel, selector, value) => {
    const el = panel.querySelector(selector);
    if (el) el.textContent = text(value);
};

// World modifiers are the server-side settings that change gameplay (for example
// the native resources modifier). The label is produced server-side; fall back to
// the raw id/value pair when a label is missing.
const renderModifiers = (panel, modifiers) => {
    const host = panel.querySelector('[data-server-modifiers]');
    if (!host) return;
    host.replaceChildren();
    const list = Array.isArray(modifiers) ? modifiers : [];
    if (list.length === 0) {
        const empty = document.createElement('span');
        empty.className = 'serverInfoModifierEmpty';
        empty.textContent = 'Sin modificadores del mundo (valores por defecto).';
        host.appendChild(empty);
        return;
    }
    list.forEach((entry) => {
        const mod = entry && typeof entry === 'object' ? entry : {};
        const chip = document.createElement('span');
        chip.className = 'serverInfoModifier';
        chip.textContent = mod.label ? text(mod.label) : text([mod.id, mod.value].filter(Boolean).join(' '));
        host.appendChild(chip);
    });
};

const appendMod = (container, mod) => {
    const article = document.createElement('article');
    article.className = 'serverInfoMod';
    const title = document.createElement('h3');
    const href = safeUrl(mod.url);
    if (href) {
        const link = document.createElement('a');
        link.href = href;
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        link.textContent = text(mod.name);
        title.appendChild(link);
    } else {
        title.textContent = text(mod.name);
    }
    article.appendChild(title);

    const details = document.createElement('dl');
    appendDefinition(details, 'Versión', mod.version ?? 'No disponible');
    appendDefinition(details, 'Estado', allowedStatuses.has(mod.status) ? mod.status : 'error');
    appendDefinition(details, 'Descripción', mod.description);
    article.appendChild(details);

    if (mod.config && typeof mod.config === 'object' && !Array.isArray(mod.config)) {
        const config = document.createElement('dl');
        config.className = 'serverInfoConfig';
        for (const [key, value] of Object.entries(mod.config)) appendDefinition(config, key, value);
        if (config.childElementCount > 0) {
            const configTitle = document.createElement('h4');
            configTitle.textContent = 'Configuración pública';
            article.append(configTitle, config);
        }
    }
    container.appendChild(article);
};

const render = (panel, payload) => {
    const server = payload && payload.server && typeof payload.server === 'object' ? payload.server : {};
    setServerText(panel, '[data-server-game-version]', server.gameVersion);
    setServerText(panel, '[data-server-world-name]', server.worldName);
    setServerText(panel, '[data-server-name]', server.serverName);
    const visibility = panel.querySelector('[data-server-visibility]');
    if (visibility) visibility.textContent = visibilityLabel(server.visibility);
    setServerText(panel, '[data-server-player-count]', server.playerCount);
    setServerText(panel, '[data-server-updated-at]', payload && payload.updatedAt);
    renderModifiers(panel, server.worldModifiers);
    const mods = panel.querySelector('[data-server-mods]');
    mods.replaceChildren();
    if (Array.isArray(payload && payload.mods)) payload.mods.forEach((mod) => appendMod(mods, mod && typeof mod === 'object' ? mod : {}));
};

export const setupServerInfo = () => {
    const panel = document.querySelector('[data-id="serverInfoView"]');
    const nav = document.querySelector('[data-id="serverInfoLink"]');
    const mapLink = document.querySelector('[data-id="mapViewLink"]');
    if (!panel || !nav || !mapLink) return;

    const show = (serverVisible) => {
        panel.hidden = !serverVisible;
        document.body.classList.toggle('serverInfoOpen', serverVisible);
        nav.setAttribute('aria-current', serverVisible ? 'page' : 'false');
    };
    const updateView = () => show(window.location.hash === '#server');
    nav.addEventListener('click', () => window.setTimeout(updateView, 0));
    mapLink.addEventListener('click', () => window.setTimeout(updateView, 0));
    window.addEventListener('hashchange', updateView);
    updateView();

    fetch('/api/server-info', { method: 'GET', credentials: 'same-origin' })
        .then((response) => response.ok ? response.json() : Promise.reject(new Error('server-info unavailable')))
        .then((payload) => render(panel, payload))
        .catch(() => {
            panel.querySelector('[data-server-status]').textContent = 'La información del servidor no está disponible.';
        });
};