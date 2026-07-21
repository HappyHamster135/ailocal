#!/usr/bin/env node
// Statisk verifiering av dashboarden (src/AiLocal.Node/Roles/Dashboard.cs):
//  1. Extraherar det inbäddade <script>-blocket ur raw-strängen och kör
//     `node --check` på det - ett JS-syntaxfel här är en helt död dashboard.
//  2. Id-driftkontroll: varje $('...')/getElementById('...') i skriptet
//     måste ha ett motsvarande id="..." i markupen (eller stå i allowlisten
//     för element som skapas dynamiskt i JS).
// Körs FÖRE varje commit som rör Dashboard.cs. Avslutar med exit 1 vid fel.
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');

const dashboardPath = path.join(__dirname, '..', 'src', 'AiLocal.Node', 'Roles', 'Dashboard.cs');
const source = fs.readFileSync(dashboardPath, 'utf8');

// ---- 1. Extrahera skriptet -------------------------------------------------
const scriptStart = source.indexOf('<script>');
const scriptEnd = source.lastIndexOf('</script>');
if (scriptStart < 0 || scriptEnd <= scriptStart) {
  console.error('FEL: hittade inget <script>-block i Dashboard.cs');
  process.exit(1);
}
const js = source.slice(scriptStart + '<script>'.length, scriptEnd);
const lineCount = js.split('\n').length;

const tmp = path.join(os.tmpdir(), `ailocal-dashboard-check-${process.pid}.js`);
fs.writeFileSync(tmp, js, 'utf8');
try {
  execFileSync(process.execPath, ['--check', tmp], { stdio: ['ignore', 'ignore', 'pipe'] });
  console.log(`SYNTAX OK (${lineCount} lines)`);
} catch (err) {
  console.error('JS-SYNTAXFEL i dashboarden:');
  console.error(String(err.stderr || err.message).replace(new RegExp(tmp.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'g'), 'Dashboard.<script>'));
  process.exit(1);
} finally {
  try { fs.unlinkSync(tmp); } catch { /* städning */ }
}

// ---- 2. Id-drift -----------------------------------------------------------
// Element som skapas/hanteras dynamiskt i JS och därför saknar statisk markup.
const dynamicOk = new Set(['updateNowBtn', 'topologyForgetHostBtn']);

const markup = source.slice(0, scriptStart);
const markupIds = new Set();
for (const m of markup.matchAll(/id="([A-Za-z0-9_-]+)"/g)) markupIds.add(m[1]);
// Id:n som skapas inne i JS-genererad markup räknas också som existerande.
for (const m of js.matchAll(/id="([A-Za-z0-9_-]+)"/g)) markupIds.add(m[1]);
for (const m of js.matchAll(/id='([A-Za-z0-9_-]+)'/g)) markupIds.add(m[1]);

const referenced = new Set();
for (const m of js.matchAll(/\$\('([A-Za-z0-9_-]+)'\)/g)) referenced.add(m[1]);
for (const m of js.matchAll(/getElementById\('([A-Za-z0-9_-]+)'\)/g)) referenced.add(m[1]);

const missing = [...referenced].filter(id => !markupIds.has(id) && !dynamicOk.has(id)).sort();
if (missing.length > 0) {
  console.error(`ID-DRIFT: ${missing.length} id:n refereras i JS men finns inte i markupen:`);
  for (const id of missing) console.error('  - ' + id);
  process.exit(1);
}
console.log(`ID DRIFT OK (${referenced.size} ids referenced)`);
