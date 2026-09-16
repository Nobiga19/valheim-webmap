import map from './map';
import { diffById, normalizeCartographyPin } from './diff';

// Cartography table pins are imported from player cartography tables and
// change slowly, so this layer polls on a leisurely timer. A 404 means an
// older server without the endpoint, and a broken payload is treated the
// same way: the layer disables itself quietly, logging once.

const POLL_INTERVAL_MS = 15000;

let mapApi = map;
let intervalId;
let disabled = false;
let logged = false;
let previous = [];
const icons = new Map();

const disable = (reason) => {
  disabled = true;
  clearInterval(intervalId);
  if (!logged) {
    logged = true;
    console.log(`cartography pins layer disabled (${reason})`);
  }
};

const upsert = (pin) => {
  let iconObj = icons.get(pin.id);
  if (!iconObj) {
    iconObj = {
      // One toggle key for the whole layer, while the iconClass reuses the
      // existing fire/house/mine/cave/dot pin glyphs (see map.js createIconEl).
      id: pin.id,
      type: 'cartography',
      iconClass: `cartography ${pin.type}`,
      text: pin.name,
      x: pin.x,
      z: pin.z
    };
    icons.set(pin.id, iconObj);
    mapApi.addIcon(iconObj, false);
  } else {
    iconObj.x = pin.x;
    iconObj.z = pin.z;
  }
};

const poll = () => {
  if (disabled) return;
  fetch('cartography/pins').then(res => {
    if (res.status === 404) {
      disable('404');
      return null;
    }
    return res.ok ? res.json() : null;
  }).then(pins => {
    if (!pins) return;
    if (!Array.isArray(pins)) {
      disable('unexpected payload');
      return;
    }
    const next = pins.map(normalizeCartographyPin).filter(Boolean);
    const { removed } = diffById(previous, next);
    removed.forEach(pin => {
      const iconObj = icons.get(pin.id);
      if (iconObj) {
        mapApi.removeIcon(iconObj);
        icons.delete(pin.id);
      }
    });
    next.forEach(upsert);
    mapApi.updateIcons();
    previous = next;
  }).catch(err => disable(err && err.message ? err.message : 'fetch error'));
};

export const init = (deps = {}) => {
  if (deps.map) {
    mapApi = deps.map;
  }
  intervalId = setInterval(poll, POLL_INTERVAL_MS);
  poll();
};

export default { init };
