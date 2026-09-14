// Load pages in a headless Chrome over the DevTools protocol and report what
// each one drew, which hosts it talked to, and any exception -- the check a
// viewer change gets before it ships. Needs Chrome listening on --remote-debugging-port.
//
//   flatpak run com.google.Chrome --headless=new --disable-gpu --remote-debugging-port=9333 \
//     --user-data-dir=/tmp/shoot-profile --window-size=1400,900 about:blank &
//   node tools/shoot.mjs http://127.0.0.1:8765 /out 9333 / /portals.html /plan.html /players.html
import { writeFileSync } from "node:fs";
const [BASE, OUT, PORT, ...PAGES] = process.argv.slice(2);
const open = async (page) => {
  const tab = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: "PUT" })).json();
  const ws = new WebSocket(tab.webSocketDebuggerUrl); await new Promise(r => ws.onopen = r);
  let id = 0; const pending = new Map(); const errors = [];
  ws.onmessage = e => { const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result ?? m.error); pending.delete(m.id); }
    if (m.method === "Runtime.exceptionThrown") errors.push((m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text).slice(0, 200)); };
  const send = (method, params = {}) => new Promise(r => { pending.set(++id, r); ws.send(JSON.stringify({ id, method, params })); });
  await send("Runtime.enable"); await send("Page.enable"); await send("Network.enable"); await send("Network.setCacheDisabled", { cacheDisabled: true });
  await send("Page.navigate", { url: BASE + page }); await new Promise(r => setTimeout(r, 15000));
  const v = (await send("Runtime.evaluate", { returnByValue: true, expression: `({title: document.title,
    hosts: [...new Set(performance.getEntriesByType('resource').map(e => new URL(e.name).host))],
    paths: [...new Set(performance.getEntriesByType('resource').map(e => new URL(e.name).pathname))],
    markers: document.querySelectorAll('#markers .marker').length, nodes: document.querySelectorAll('.node').length, cards: document.querySelectorAll('.card, .player').length})` })).result?.value;
  writeFileSync(`${OUT}/${page.replace(/\W/g, "") || "index"}.png`, Buffer.from((await send("Page.captureScreenshot", { format: "png" })).data, "base64"));
  ws.close(); return { page, ...v, errors };
};
for (const p of PAGES.length ? PAGES : ["/"]) console.log(JSON.stringify(await open(p)));
process.exit(0);
