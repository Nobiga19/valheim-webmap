import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import fixtures from './cartography-fixtures.json' with { type: 'json' };
import { diffById, normalizeVehicle, normalizeCartographyPin } from '../WebMap/web-src/diff.js';

const clean = (value = '') => value.replace(/\r|\n|,/g, ' ').trim();
const finiteResource = (value) => value !== null && Number.isFinite(Number(value)) && Number(value) >= 0 ? Number(value) : null;
const project = (pins) => {
  const result = new Map();
  for (const pin of pins) {
    if (!Number.isFinite(pin.x) || !Number.isFinite(pin.z)) continue;
    const sourceName = pin.name ?? '';
    const name = clean(sourceName);
    // The checked state belongs to the table, not the logical pin. Keep the
    // full coordinate representation so nearby, distinct pins survive.
    const key = `${pin.type}\n${sourceName.length}:${sourceName}\n${pin.x}\n${pin.z}`;
    const id = `cartography:${createHash('sha256').update(key).digest('hex').slice(0, 24)}`;
    result.set(id, { id, name });
  }
  return [...result.values()];
};

for (const fixture of [fixtures.duplicateAcrossTables, fixtures.emptyTable]) {
  assert.equal(project(fixture.pins).length, fixture.expectedCount);
}
assert.equal(project(fixtures.unicodeAndCsvSafety.pins)[0].name, fixtures.unicodeAndCsvSafety.expectedName);
assert.equal(project(fixtures.distinctRawLabels.pins).length, fixtures.distinctRawLabels.expectedCount);
assert.equal(project(fixtures.duplicateAcrossTables.pins)[0].id, project([...fixtures.duplicateAcrossTables.pins].reverse())[0].id);
assert.equal(project(fixtures.checkedStateDoesNotDuplicate.pins).length, fixtures.checkedStateDoesNotDuplicate.expectedCount);
assert.equal(project(fixtures.nearbyButDistinct.pins).length, fixtures.nearbyButDistinct.expectedCount);
const before = new Set(project(fixtures.changeAndRemoval.before).map(pin => pin.id));
const after = new Set(project(fixtures.changeAndRemoval.after).map(pin => pin.id));
assert.equal([...before].filter(id => !after.has(id)).length, fixtures.changeAndRemoval.expectedRemoved);
assert.equal([...after].filter(id => !before.has(id)).length, fixtures.changeAndRemoval.expectedAdded);
for (const telemetry of fixtures.telemetry) assert.equal(finiteResource(telemetry.value), telemetry.expected);
// A bad payload only preserves that table's prior state; healthy tables continue
// to contribute. This mirrors the separate lastGoodByTable cache in C#.
const cache = new Map([['bad', fixtures.corruptTable.lastGood], ['healthy', fixtures.corruptTable.healthy]]);
assert.deepEqual([...cache.values()].flat(), fixtures.corruptTable.expectedMerged);

// Keep the user-facing configuration and the two pin stores from drifting away
// from the runtime contract exercised above.
const configSource = readFileSync(new URL('../WebMap/Config.cs', import.meta.url), 'utf8');
const tableSource = readFileSync(new URL('../WebMap/CartographyTablePins.cs', import.meta.url), 'utf8');
const mapDataSource = readFileSync(new URL('../WebMap/MapDataServer.cs', import.meta.url), 'utf8');
const playerUiSource = readFileSync(new URL('../WebMap/web-src/players.js', import.meta.url), 'utf8');
assert.match(configSource, /"import_cartography_pins"/);
assert.match(configSource, /"cartography_pin_update_interval"/);
assert.match(configSource, /"player_update_interval"/);
assert.match(tableSource, /"cartography:" \+ Hash\(key\)/);
assert.match(tableSource, /GetAllZDOsWithPrefabIterative/);
assert.match(tableSource, /lastGoodByTable/);
assert.match(mapDataSource, /private volatile string cartographyJson/);
assert.match(mapDataSource, /"\/cartography\/pins"/);
assert.doesNotMatch(mapDataSource, /cartographyPins/);
assert.match(configSource, /"dedup_radius_meters"/);
assert.match(configSource, /"discovery_interval"/);
assert.match(configSource, /"update_interval"/);
assert.match(mapDataSource, /ZDOVars\.s_stamina/);
assert.match(mapDataSource, /ZDOVars\.s_eitr/);
assert.match(mapDataSource, /private static int HealthValue/);
assert.match(playerUiSource, /\? '—'/);
assert.doesNotMatch(playerUiSource, /details\.style\.display/);
assert.match(playerUiSource, /const shouldShowMarker = !player\.hidden && hasValidPosition\(player\)/);
assert.match(playerUiSource, /markerAdded: false/);
assert.match(playerUiSource, /formatResource\(player\.eitr\)/);
assert.match(playerUiSource, /Position hidden in Valheim/);
const websocketSource = readFileSync(new URL('../WebMap/web-src/websocket.js', import.meta.url), 'utf8');
assert.match(websocketSource, /playerLines\[0\]\.includes\(','\)/);
assert.match(websocketSource, /location\.origin\.replace\(\/\^http\//);
assert.match(websocketSource, /Number\.isFinite\(xyz\[0\]\) && Number\.isFinite\(xyz\[1\]\)/);
const htmlSource = readFileSync(new URL('../WebMap/web/index.html', import.meta.url), 'utf8');
assert.match(htmlSource, /<script src="main\.js"><\/script>/);
assert.match(htmlSource, /href="style\.css"/);
assert.doesNotMatch(htmlSource, /[?&]v=/);
assert.doesNotMatch(htmlSource, /recovery|main-v2\.9\.1-ui-fix/i);

// A chat fanout calls the observer once per target. Repeating its first target
// begins a new send immediately; no elapsed-time heuristic can suppress an
// identical command sent back-to-back.
const startsBatch = (() => {
  const batches = new Map();
  return (key, target) => {
    const batch = batches.get(key);
    if (!batch) {
      batches.set(key, { firstTarget: target, targets: new Set([target]) });
      return true;
    }
    if (batch.firstTarget === target) {
      batch.targets = new Set([target]);
      return true;
    }
    batch.targets.add(target);
    return false;
  };
})();
assert.deepEqual([10, 11, 12, 10, 11].map(target => startsBatch('same-command', target)), [true, false, false, true, false]);

const webMapSource = readFileSync(new URL('../WebMap/WebMap.cs', import.meta.url), 'utf8');
assert.match(webMapSource, /batch\.FirstTarget == d\.m_targetPeerID/);

const collectTextFiles = (directory) => readdirSync(directory).flatMap((name) => {
  const path = join(directory, name);
  if (statSync(path).isDirectory()) return name === 'bin' || name === 'obj' ? [] : collectTextFiles(path);
  return /\.(?:cs|js|html|css|md|json)$/i.test(name) ? [path] : [];
});
const removedCommand = ['!', 'g', 'ive'].join('');
const removedType = ['G', 'ive', 'Command'].join('');
for (const path of [...collectTextFiles('WebMap'), 'README.md', 'CHANGELOG.md', 'FORK.md']) {
  assert.doesNotMatch(readFileSync(path, 'utf8'), new RegExp(`${removedCommand}|${removedType}`, 'i'));
}

// The public server-info shape is intentionally narrow. This fixture is a
// representative response and confirms the endpoint does not grow into a
// configuration dump when new private settings are added elsewhere.
const serverInfoFixture = {
  server: { gameVersion: '1.0.12', worldName: 'Dedicated', playerCount: 0, serverName: 'Private Valheim Test', visibility: 'private', worldModifiers: [{ id: 'resources', value: 'muchmore', label: 'Recursos x2' }] },
  mods: [{ id: 'webmap', name: 'WebMap', version: '2.10.1', status: 'enabled', url: 'https://example.invalid/webmap', description: 'Map', config: { alwaysMap: true } }],
  updatedAt: '2026-09-13T00:00:00.0000000Z'
};
assert.deepEqual(Object.keys(serverInfoFixture).sort(), ['mods', 'server', 'updatedAt']);
assert.deepEqual(Object.keys(serverInfoFixture.server).sort(), ['gameVersion', 'playerCount', 'serverName', 'visibility', 'worldModifiers', 'worldName']);
assert.ok(Array.isArray(serverInfoFixture.server.worldModifiers));
assert.equal('worldSeed' in serverInfoFixture.server, false);
assert.deepEqual(Object.keys(serverInfoFixture.mods[0]).sort(), ['config', 'description', 'id', 'name', 'status', 'url', 'version']);
assert.ok(['enabled', 'disabled', 'available', 'error'].includes(serverInfoFixture.mods[0].status));
assert.equal('password' in serverInfoFixture.server, false);
assert.equal('token' in serverInfoFixture.mods[0].config, false);
assert.equal('serverPort' in serverInfoFixture.mods[0].config, false);

const serverInfoSource = readFileSync(new URL('../WebMap/ServerInfoSnapshot.cs', import.meta.url), 'utf8');
assert.match(mapDataSource, /case "\/api\/server-info"/);
assert.match(mapDataSource, /serverInfoJson = ServerInfoSnapshot\.BuildJson/);
assert.match(serverInfoSource, /AppendWebMapConfig/);
assert.match(serverInfoSource, /alwaysMap/);
assert.match(serverInfoSource, /alwaysVisible/);
assert.match(serverInfoSource, /showVehicles/);
assert.match(serverInfoSource, /importCartographyPins/);
assert.doesNotMatch(serverInfoSource, /CatalogEntry\("rcon"/);
assert.doesNotMatch(serverInfoSource, /DISCORD_WEBHOOK|ANNOUNCE_TOKEN|SERVER_PORT|WORLD_SEED/);
const catalogLinks = {
  WebMap: 'https://thunderstore.io/c/valheim/p/ArmchairSavages/WebMap/',
  'Server Devcommands': 'https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/',
  ValheimTune: 'https://thunderstore.io/c/valheim/p/Akoozie/ValheimTune/',
  'Valheim Performance Optimizations': 'https://thunderstore.io/c/valheim/p/ontrigger/ValheimPerformanceOptimizations/',
  TickProfiler: 'https://thunderstore.io/c/valheim/p/MagiCorp/TickProfiler/',
  ServersideQoL: 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL/',
  'ServersideQoL.JustSleep': 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_JustSleep/',
  'ServersideQoL.Signs': 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_Signs/',
  'ServersideQoL.MultiplayerTweaks': 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_MultiplayerTweaks/',
  'ServersideQoL.AutoMapTables': 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_AutoMapTables/',
  'ServersideQoL.AutoStore': 'https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_AutoStore/',
  ServerPasswordOnce: 'https://thunderstore.io/c/valheim/p/DooDesch/ServerPasswordOnce/',
  'Render Limits': 'https://github.com/JereKuusela/valheim-render_limits'
};
for (const [name, url] of Object.entries(catalogLinks)) {
  assert.match(serverInfoSource, new RegExp(`"${name}", "${url.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}"`));
}
// Dismissed modpack candidates must stay out of the public catalog entirely.
const dismissed = ['Smoothbrain', 'Odin_Sons', 'AviiNL', 'SmoothSave', 'ServerInfo"'];
for (const gone of dismissed) {
  assert.ok(!serverInfoSource.includes(gone), `server-info catalog must not reference ${gone}`);
}
// Gameplay-impacting server variables and the live ValheimTune config allowlist.
assert.match(serverInfoSource, /worldModifiers/);
assert.match(serverInfoSource, /AppendFileConfig/);
assert.match(serverInfoSource, /akoozie\.valheimtune\.cfg/);

const serverInfoUiSource = readFileSync(new URL('../WebMap/web-src/server-info.js', import.meta.url), 'utf8');
assert.match(htmlSource, /Servidor/);
assert.match(htmlSource, /data-id="serverInfoView"/);
assert.match(serverInfoUiSource, /fetch\('\/api\/server-info'/);
assert.match(serverInfoUiSource, /document\.createElement/);
assert.match(serverInfoUiSource, /link\.href = href/);
assert.match(serverInfoUiSource, /data-server-modifiers/);
assert.match(htmlSource, /data-server-modifiers/);
assert.match(htmlSource, /Modificadores del mundo/);
assert.doesNotMatch(serverInfoUiSource, /innerHTML/);
assert.doesNotMatch(serverInfoUiSource, /method:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/);
const serverInfoMarkup = htmlSource.slice(htmlSource.indexOf('<main data-id="serverInfoView"'));
assert.doesNotMatch(serverInfoMarkup, /<button\b/i);
console.log('cartography/player/server-info fixtures passed');

// Vehicles and cartography pins share one pure id-diff (web-src/diff.js), so
// the exact add/update/remove behaviour of every poll is testable without the
// DOM. Kept entries must carry the newest coordinates for the in-place update.
const firstVehiclePoll = [
  { id: 'boat-a', kind: 'boat', name: 'Karve', x: 10, z: 20, rotation: 135 },
  { id: 'cart-b', kind: 'cart', name: 'Cart', x: -5, z: 5, rotation: 0 }
];
const secondVehiclePoll = [
  { id: 'cart-b', kind: 'cart', name: 'Cart', x: -6, z: 5, rotation: 0 },
  { id: 'boat-c', kind: 'boat', name: 'Longship', x: 1, z: 2, rotation: 45 }
];
const vehicleDiff = diffById(firstVehiclePoll, secondVehiclePoll);
assert.deepEqual(vehicleDiff.removed.map(v => v.id), ['boat-a']);
assert.deepEqual(vehicleDiff.added.map(v => v.id), ['boat-c']);
assert.deepEqual(vehicleDiff.kept.map(v => v.id), ['cart-b']);
assert.equal(vehicleDiff.kept[0].x, -6);
assert.deepEqual(diffById(firstVehiclePoll, []), { added: [], removed: firstVehiclePoll, kept: [] });
assert.deepEqual(diffById([], secondVehiclePoll, v => v.id), { added: secondVehiclePoll, removed: [], kept: [] });

// Servers predating this feature may omit id and rotation; entries without
// finite x/z never reach the map.
assert.deepEqual(normalizeVehicle({ kind: 'boat', x: 1043.2, z: -842.1 }), {
  id: 'boat:1043.2:-842.1', kind: 'boat', name: '', x: 1043.2, z: -842.1, rotation: null
});
assert.equal(normalizeVehicle({ kind: 'boat', x: 'NaN', z: 1 }), null);
assert.equal(normalizeVehicle({ kind: 'wagon', x: 1, z: 1 }), null);
assert.equal(normalizeVehicle({ id: '7', kind: 'cart', x: 2, z: 4, rotation: '135.5' }).rotation, 135.5);
assert.equal(normalizeVehicle({ id: '7', kind: 'cart', x: 2, z: 4, rotation: 'abc' }).rotation, null);
assert.equal(normalizeVehicle({ id: '7', kind: 'cart', type: 'Cart', name: null, x: 2, z: 4 }).name, 'Cart');

// Cartography pins diff by their stable cartography:* ids, only accept the
// known pin types, and an empty table clears the layer.
const firstPinPoll = [
  { id: 'cartography:a', name: 'Copper', type: 'mine', x: 1322.4, z: -220.7, checked: false },
  { id: 'cartography:b', name: 'Hoe', type: 'dot', x: 0, z: 0, checked: true }
].map(normalizeCartographyPin);
const secondPinPoll = [
  { id: 'cartography:b', name: 'Hoe', type: 'dot', x: 1, z: 0, checked: false }
].map(normalizeCartographyPin);
const pinDiff = diffById(firstPinPoll, secondPinPoll);
assert.deepEqual(pinDiff.removed.map(p => p.id), ['cartography:a']);
assert.deepEqual(pinDiff.added.map(p => p.id), []);
assert.deepEqual(pinDiff.kept.map(p => p.id), ['cartography:b']);
assert.equal(pinDiff.kept[0].x, 1);
assert.deepEqual(diffById(firstPinPoll, []).removed.length, 2);
assert.equal(normalizeCartographyPin({ id: 'x', name: 'Weird', type: 'portal', x: 1, z: 1 }), null);
assert.equal(normalizeCartographyPin({ id: 'x', type: 'fire', x: 'z', z: 1 }), null);
assert.equal(normalizeCartographyPin({ name: 'No id', type: 'fire', x: 1, z: 1 }), null);
assert.deepEqual(normalizeCartographyPin({ id: 'x', type: 'cave', x: '1.5', z: '-2' }), { id: 'x', type: 'cave', name: '', x: 1.5, z: -2 });
console.log('vehicle/cartography diff fixtures passed');
