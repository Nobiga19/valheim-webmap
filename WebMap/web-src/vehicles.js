import map from './map';
import { diffById, normalizeVehicle } from './diff';

// Boats and carts only exist in the running game, so the viewer polls
// /vehicles and diffs by id. One interval means requests cannot stack;
// transport hiccups are simply retried on the next tick.

const POLL_INTERVAL_MS = 5000;

let mapApi = map;
let previous = [];
const icons = new Map();

const setHeading = (iconObj) => {
  if (iconObj.node && iconObj.rotation !== null) {
    // The sprite is drawn bow-up. On this map yaw 0 (+z) is screen-up, so a
    // plain clockwise CSS rotation of the inner node matches the boat heading
    // while map.js keeps owning left/top and the translate(-50%, -50%)
    // anchoring of the outer element.
    iconObj.node.style.transform = `rotate(${iconObj.rotation}deg)`;
  }
};

const upsert = (vehicle) => {
  let iconObj = icons.get(vehicle.id);
  if (!iconObj) {
    iconObj = {
      id: vehicle.id,
      type: vehicle.kind,
      text: vehicle.name,
      x: vehicle.x,
      z: vehicle.z,
      kind: vehicle.kind,
      rotation: vehicle.rotation
    };
    if (vehicle.kind === 'boat') {
      // Rotate an inner node so map.js never has to touch the marker el.
      iconObj.node = document.createElement('div');
      iconObj.node.className = 'vehicleSprite';
    }
    icons.set(vehicle.id, iconObj);
    mapApi.addIcon(iconObj, false);
  } else {
    iconObj.x = vehicle.x;
    iconObj.z = vehicle.z;
    iconObj.rotation = vehicle.rotation;
    if (iconObj.text !== vehicle.name) {
      if (iconObj.el) {
        iconObj.el.querySelector('.text').textContent = vehicle.name;
      }
      iconObj.text = vehicle.name;
    }
  }
  setHeading(iconObj);
};

const poll = () => {
  fetch('vehicles').then(res => res.ok ? res.json() : null).then(data => {
    if (!data || !Array.isArray(data.vehicles)) return;
    const next = data.vehicles.map(normalizeVehicle).filter(Boolean);
    const { removed } = diffById(previous, next);
    removed.forEach(vehicle => {
      const iconObj = icons.get(vehicle.id);
      if (iconObj) {
        mapApi.removeIcon(iconObj);
        icons.delete(vehicle.id);
      }
    });
    next.forEach(upsert);
    mapApi.updateIcons();
    previous = next;
  }).catch(() => { });
};

export const init = (deps = {}) => {
  if (deps.map) {
    mapApi = deps.map;
  }
  setInterval(poll, POLL_INTERVAL_MS);
  poll();
};

export default { init };
