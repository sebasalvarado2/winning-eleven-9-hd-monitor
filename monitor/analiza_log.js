const fs = require('fs');
const f = process.argv[2];
const lines = fs.readFileSync(f, 'utf8').split(/\r?\n/);
const out = [];
let lastBall = '';
for (const l of lines) {
  let m;
  if ((m = l.match(/^(\S+) (?:\[[^\]]*\] )?PARTIDO nivel0.1.2.3 = (\S+) -> (\S+)/))) {
    const a = m[2].split('.'), b = m[3].split('.');
    if (a[1] !== b[1]) out.push(`${m[1]} FASE ${m[2]} -> ${m[3]}`);
  } else if ((m = l.match(/^(\S+) (?:\[[^\]]*\] )?PELOTA estado (-?\d+) -> (-?\d+)\s+X=(-?\d+) Z=(-?\d+)/))) {
    lastBall = `X=${m[4]} Z=${m[5]}`;
    out.push(`${m[1]}    pelota ${m[2]}->${m[3]} ${lastBall}`);
  } else if (/ULTIMA_ACCION/.test(l)) out.push(l);
  else if (/MODO_JUEGO/.test(l)) out.push(l);
}
fs.writeFileSync(process.argv[3], out.join('\n'));
console.log(out.length + ' lineas');
