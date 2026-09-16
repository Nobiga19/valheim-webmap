// Shared pure helpers for the polled map layers (vehicles, cartography pins).
// Keeping the id-diff and payload normalisation here lets node tests exercise
// the exact add/update/remove logic without touching the DOM.

export const diffById = (previous = [], next = [], keyFn = item => item.id) => {
  const prevKeys = new Set(previous.map(keyFn));
  const nextKeys = new Set(next.map(keyFn));
  return {
    added: next.filter(item => !prevKeys.has(keyFn(item))),
    removed: previous.filter(item => !nextKeys.has(keyFn(item))),
    kept: next.filter(item => prevKeys.has(keyFn(item)))
  };
};

const VEHICLE_KINDS = ['boat', 'cart'];

export const normalizeVehicle = (entry) => {
  if (!entry || !VEHICLE_KINDS.includes(entry.kind)) return null;
  const x = Number(entry.x);
  const z = Number(entry.z);
  if (!Number.isFinite(x) || !Number.isFinite(z)) return null;
  const rotation = Number(entry.rotation);
  return {
    // Servers older than this feature may omit id and rotation entirely.
    id: typeof entry.id === 'string' && entry.id.length > 0 ? entry.id : `${entry.kind}:${x}:${z}`,
    kind: entry.kind,
    name: entry.name || entry.type || '',
    x,
    z,
    rotation: Number.isFinite(rotation) ? rotation : null
  };
};

const PIN_TYPES = ['fire', 'house', 'mine', 'cave', 'dot'];

export const normalizeCartographyPin = (entry) => {
  if (!entry || typeof entry.id !== 'string' || entry.id.length === 0) return null;
  if (!PIN_TYPES.includes(entry.type)) return null;
  const x = Number(entry.x);
  const z = Number(entry.z);
  if (!Number.isFinite(x) || !Number.isFinite(z)) return null;
  return { id: entry.id, type: entry.type, name: entry.name ?? '', x, z };
};
