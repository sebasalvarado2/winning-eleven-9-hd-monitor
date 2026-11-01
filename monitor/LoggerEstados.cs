// LoggerEstados - registra los estados internos del partido de WE9 (SOLO LECTURA, no escribe nada en el juego).
// Sirve para ubicar eventos (falta, tiro libre, corner, gol...) con las tablas de ingenieria inversa:
//   partido  : 0xFF078C (10 niveles; ver re_estados_partido.txt)
//   pelota   : [0xC20834]+0x80 (17 estados; ver re_estado_pelota.txt)
//   jugador  : 0x3BCF880 + i*0x290, +0x42 estado, +0x44 subestado (ver re_estado_jugador.txt)
// Salida: logs\estados_partido.txt  (una linea por cambio, con hora)
// Uso: LoggerEstados.exe [-todo]   (-todo = loguea tambien los cambios de estado de jugadores comunes)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Native
{
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    public const int PROCESS_VM_READ = 0x0010, PROCESS_QUERY_INFORMATION = 0x0400;
}

class Logger
{
    static IntPtr h;
    static byte[] Read(long addr, int n)
    {
        byte[] b = new byte[n]; int r;
        if (!Native.ReadProcessMemory(h, new IntPtr(addr), b, n, out r) || r != n) return null;
        return b;
    }
    static int I32(long a) { byte[] b = Read(a, 4); return b == null ? 0 : BitConverter.ToInt32(b, 0); }
    static float F32(long a) { byte[] b = Read(a, 4); return b == null ? 0f : BitConverter.ToSingle(b, 0); }

    static StreamWriter log, evlog;
    static int sc0 = -1, sc1 = -1, clk = -1;   // marcador (shorts 0x3B86FA4 + eq*0x744) y reloj (0x3BE0F74, frames restantes)
    static string Ctx() { return "[" + sc0 + "-" + sc1 + " R=" + clk + "]"; }
    static void W(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + Ctx() + " " + s;
        log.WriteLine(line); log.Flush();
        Console.WriteLine(line);
    }
    // evento ya identificado (ver re_notas.md vueltas 84-85): va a eventos_partido.txt y tambien al log crudo con "##"
    static void E(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + Ctx() + " " + s;
        evlog.WriteLine(line); evlog.Flush();
        W("## " + s);
    }
    // nombres de nivel1 confirmados por log (CPU vs CPU 30/10/2026)
    static string N1(int n)
    {
        switch (n)
        {
            case 0: return "SAQUE_CENTRO";
            case 1: return "JUEGO";
            case 2: return "TIRO_LIBRE";
            case 3: return "LATERAL";
            case 4: return "CORNER";
            case 6: return "FALTA";
            case 7: return "GOL";
            case 9: return "SAQUE_ARCO";
            case 10: return "FIN_TIEMPO";
            case 11: return "REPETICION";
            case 12: return "OFFSIDE";
            case 14: return "RESUMEN_JUGADAS";
            case 16: return "PARADA_POR_LESION [H]";
            case 17: return "CAMBIOS(pantalla de sustitucion) [H]";
            case 27: return "FASE_27(previa a repeticion de offside?) [H]";
            default: return "FASE_" + n + "(sin nombre)";
        }
    }
    // nombre real del jugador: ID del option file en el bloque (+0x3C, u16) -> tabla de jugadores [0x38B77C4] + id*0x7C (nombre UTF-16 en +0)
    static Dictionary<int, string> nombres = new Dictionary<int, string>();
    static int nombresT = 0;
    static string NombreId(int id)
    {
        if (id <= 0) return "";
        if (Environment.TickCount - nombresT > 10000) { nombres.Clear(); nombresT = Environment.TickCount; }  // el juego carga los nombres editados despues de arrancar
        string s;
        if (nombres.TryGetValue(id, out s)) return s;
        s = "";
        int tb = I32(0x38B77C4);
        if (tb != 0)
        {
            byte[] nb = Read((long)tb + id * 0x7CL, 0x20);
            if (nb != null)
            {
                s = Encoding.Unicode.GetString(nb);
                int z = s.IndexOf('\0'); if (z >= 0) s = s.Substring(0, z);
                StringBuilder sb = new StringBuilder();
                foreach (char c in s) sb.Append(c < 32 ? '?' : c);
                s = sb.ToString();
            }
        }
        nombres[id] = s;
        return s;
    }
    static string NombreSlot(int i)
    {
        if (i <= 0 || i > 22) return "";
        byte[] b = Read(0x3BCF880L + i * 0x290L + 0x3C, 2);
        return b == null ? "" : NombreId(BitConverter.ToUInt16(b, 0));
    }
    static string Quien(int i)
    {
        // el equipo sale del propio bloque (+0x40): en el 2o tiempo los lados se invierten y los bloques 1-11 pasan a ser del equipo 1
        string b = i == 0 ? "arbitro" : (i > 22 ? "slot" + i : "eq" + slotEq[i] + " slot" + i);
        string n = NombreSlot(i);
        return n.Length > 0 ? b + " " + n : b;
    }
    // ---- lista de eventos del juego ("Game history"): [0xFF694C] + 0x12EC, registros de largo variable terminados en 0 (re_notas.md vueltas 88-89)
    static int[] slotEq = new int[23], slotIdx = new int[23];   // equipo (+0x40) e indice de eventos (+0x3F) de cada bloque 1..22
    static string NJ(int b)
    {
        int eq = (b >> 7) & 1, ind = b & 0x7F;
        for (int s = 1; s <= 22; s++)
            if (slotEq[s] == eq && slotIdx[s] == ind) return "eq" + eq + " " + NombreSlot(s) + " (slot" + s + ")";
        return "eq" + eq + " idx" + ind + "?";
    }
    static int EquipoDeId(int id)
    {
        for (int s = 1; s <= 22; s++)
        {
            byte[] b = Read(0x3BCF880L + s * 0x290L + 0x3C, 2);
            if (b != null && BitConverter.ToUInt16(b, 0) == id) return slotEq[s];
        }
        return -1;
    }
    static string Min(int b) { return (b & 0x7F) + "'" + ((b & 0x80) != 0 ? "+" : ""); }
    static List<byte[]> Recorrer(byte[] buf)
    {
        List<byte[]> regs = new List<byte[]>();
        for (int i = 0; i < buf.Length && buf[i] != 0; )
        {
            int c = buf[i], n = 4;
            if (c >= 0x0B && c <= 0x10) n = 8;
            else if (c >= 0x11 && c <= 0x1A) { if (i + 3 >= buf.Length) break; n = 4 + 4 * buf[i + 3]; }
            if (i + n > buf.Length) break;
            byte[] r = new byte[n]; Array.Copy(buf, i, r, 0, n); regs.Add(r);
            i += n;
        }
        return regs;
    }
    static string DecEvento(byte[] r)
    {
        int c = r[0];
        switch (c)
        {
            case 0x01: return "SAQUE_INICIAL " + NJ(r[1]) + " " + Min(r[2]);
            // 04 y 05 NO son saques de arco confirmados: aparecen sin que la pelota salga ni haya fase 9 (vuelta 92)
            case 0x04: return "REGISTRO_04(sin significado confirmado) " + NJ(r[1]) + " " + Min(r[2]);
            case 0x05: return "REGISTRO_05(sin significado confirmado) " + NJ(r[1]) + " " + Min(r[2]);
            case 0x06: return "CORNER(derecho) " + NJ(r[1]) + " " + Min(r[2]);
            case 0x07: return "CORNER(izquierdo) " + NJ(r[1]) + " " + Min(r[2]);
            case 0x08: return "TIRO_LIBRE " + NJ(r[1]) + " " + Min(r[2]);
            case 0x09: return "TIRO_LIBRE_POR_OFFSIDE " + NJ(r[1]) + " " + Min(r[2]);
            case 0x03:
                if (r[3] == 0x10) return "OFFSIDE " + NJ(r[1]) + " " + Min(r[2]);
                if ((r[3] & 0x80) != 0)
                {
                    string t = r[3] == 0x80 ? "" : (r[3] == 0x82 ? " + AMARILLA" : (r[3] == 0x86 ? " + SEGUNDA_AMARILLA(roja)" : (r[3] == 0x81 ? " (flags 0x81: significado desconocido, sin tarjeta)" : " flags=0x" + r[3].ToString("X2") + "?")));
                    return "FALTA_COMETIDA " + NJ(r[1]) + " " + Min(r[2]) + t;
                }
                return "FALTA?(flags 0x" + r[3].ToString("X2") + ") " + NJ(r[1]) + " " + Min(r[2]);
            case 0x0C: case 0x0D: case 0x0E:
            {
                int eq = (r[1] >> 7) & 1;
                string asis = r[3] == 0xFF ? "sin asistencia" : "asistencia de " + NJ((eq << 7) | (r[3] & 0x7F));
                string tipo = c == 0x0C ? "GOL" : (c == 0x0E ? "GOL_DE_TIRO_LIBRE" : "GOL(0D, penal?)");
                return tipo + " " + NJ(r[1]) + " " + Min(r[2]) + "  (" + asis + ")  marcador " + r[4] + "-" + r[5];
            }
            case 0x10: return "GOL_EN_CONTRA " + NJ(r[1]) + " " + Min(r[2]) + "  marcador " + r[4] + "-" + r[5];
        }
        if (c >= 0x12 && c <= 0x1A)
        {
            string fin;
            switch (c)
            {
                case 0x12: fin = "gol(penal?)"; break;
                case 0x13: fin = "tiro libre directo"; break;
                case 0x14: fin = "GOL de tiro libre"; break;
                case 0x15: fin = "atajado con rebote"; break;
                case 0x16: fin = "desviado/bloqueado"; break;
                case 0x17: fin = "GOL"; break;
                case 0x18: fin = "atajado"; break;
                case 0x19: fin = "afuera"; break;
                case 0x1A: fin = "GOL"; break;
                default: fin = "?"; break;
            }
            string quien = "?", con = "";
            int n = r[3];
            for (int k = 0; k < n && 8 + 4 * k <= r.Length; k++)
            {
                int tr = r[4 + 4 * k];
                if (tr == 0x02 || tr == 0x03 || tr == 0x04)
                {
                    quien = NJ(r[5 + 4 * k]);
                    con = tr == 0x02 ? "pie izq" : (tr == 0x03 ? "pie der" : "cabeza");
                    break;
                }
            }
            return "TIRO " + fin + " por " + quien + " (" + con + ") " + Min(r[2]) + "  arquero rival: " + NJ(r[1]) + "  tramos=" + n;
        }
        return null;   // 0x0B posesion y desconocidos: no se escribe en eventos
    }
    // ficha del jugador: tabla [0x38B77C4] + id*0x7C en formato option file (campo = u16 en rec+0x2F+off >> sh & mask)
    static int Campo(byte[] rec, int off, int sh, int mask) { return ((rec[0x2F + off] | (rec[0x2F + off + 1] << 8)) >> sh) & mask; }
    static string[] POS = { "GK", "?1", "CWP", "CB", "SB", "DMF", "WB", "CMF", "SMF", "AMF", "WF", "SS", "CF" };
    static string[] ESTAD = { "ATK", "DEF", "BAL", "STA", "SPD", "ACC", "RES", "AGI", "DRA", "DRS", "SPA", "SPS", "LPA", "LPS", "SHA", "SHP", "SHT", "FKA", "SWV", "HEA", "JMP", "TEC", "AGG", "MEN", "GK", "TW" };
    static int[,] ESTADOFF = { {7,0},{7,7},{9,0},{9,7},{10,6},{11,5},{13,0},{13,7},{14,6},{15,5},{17,0},{17,7},{18,6},{19,5},{21,0},{21,7},{22,6},{23,5},{25,0},{25,7},{26,6},{29,0},{29,7},{30,6},{31,5},{33,0} };
    static string[] HAB = { "Regate", "RegateTactico", "Posicion", "Reaccion", "Mando", "Pase", "Goleador", "Gol1a1", "Poste", "Lineas", "DispMedios", "Lado", "Centro", "Penales", "Pase1T", "Exterior", "Marcaje", "Barrida", "Cobertura", "LineaDef", "PortPenal", "Port1a1", "SaqueLargo" };
    static int[,] HABOFF = { {24,4},{24,5},{24,6},{24,7},{28,4},{28,5},{28,6},{28,7},{32,4},{32,5},{32,6},{32,7},{35,5},{35,6},{35,7},{36,0},{36,1},{36,2},{36,3},{36,4},{36,5},{36,6},{36,7} };
    static string Ficha(int id)
    {
        int tb = I32(0x38B77C4); if (tb == 0) return "";
        byte[] rec = Read((long)tb + id * 0x7CL, 0x7C); if (rec == null) return "";
        int pos = Campo(rec, 6, 4, 15);
        StringBuilder sb = new StringBuilder();
        sb.Append(pos >= 0 && pos < POS.Length ? POS[pos] : "pos" + pos);
        sb.Append(" ").Append(Campo(rec, 41, 0, 63) + 148).Append("cm ").Append(Campo(rec, 62, 5, 31) + 15).Append("a pie").Append(Campo(rec, 5, 0, 1) == 0 ? "D" : "I").Append(" |");
        for (int i = 0; i < ESTAD.Length; i++) sb.Append(" ").Append(ESTAD[i]).Append("=").Append(Campo(rec, ESTADOFF[i, 0], ESTADOFF[i, 1], 127));
        sb.Append(" | hab:");
        for (int i = 0; i < HAB.Length; i++) if (Campo(rec, HABOFF[i, 0], HABOFF[i, 1], 1) != 0) sb.Append(" ").Append(HAB[i]);
        return sb.ToString();
    }
    // tipo de accion de la pelota (pelota+0x80 y pelota+0xC4): nombres del proyecto externo we9-pes6 [E], sin confirmar en este exe
    static string Accion(int t)
    {
        switch (t)
        {
            case 0x00: return "control/recepcion";
            case 0x01: return "pase";
            case 0x02: return "centro parado/despeje pie";
            case 0x03: return "centro en carrera";
            case 0x04: return "TIRO";
            case 0x05: return "recepcion";
            case 0x06: return "corner/saque con pie";
            case 0x07: return "pelota parada/saque arco";
            case 0x08: return "despeje cabeza";
            case 0x09: return "rebote arquero";
            case 0x0A: return "despeje(0A)";
            case 0x0B: return "contacto defensivo";
            case 0x0C: return "arquero con pelota";
            case 0x0D: return "pelota suelta(0D)";
            case 0x0E: return "falta recibida(0E)";
            case 0x0F: return "quite";
            case 0x10: return "saque inicial(10)";
            default: return "?";
        }
    }
    // cuantas entradas nuevas entraron en un historial (la mas nueva primero): menor k con prev[j]==cur[j+k]. stride>0: cada entrada tambien tiene cur[i+stride]
    static int Nuevos(byte[] prev, byte[] cur, int n, int stride)
    {
        if (prev == null) return 0;
        for (int k = 0; k <= n; k++)
        {
            bool ok = true;
            for (int j = 0; j + k < n && ok; j++)
            {
                if (prev[j] != cur[j + k]) ok = false;
                else if (stride > 0 && prev[j + stride] != cur[j + k + stride]) ok = false;
            }
            if (ok) return k;
        }
        return n;
    }

    static void Main(string[] args)
    {
        bool todo = Array.IndexOf(args, "-todo") >= 0;
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(dir);
        log = new StreamWriter(Path.Combine(dir, "estados_partido.txt"), true, new UTF8Encoding(false));
        evlog = new StreamWriter(Path.Combine(dir, "eventos_partido.txt"), true, new UTF8Encoding(false));
        Console.WriteLine("LoggerEstados - esperando WE9.exe ... (Ctrl+C para salir)");

        HashSet<int> comunes = new HashSet<int>(new int[] { 0x00, 0x01, 0x07, 0x18, 0x1B, 0x31 });
        while (true)
        {
            Process[] ps = Process.GetProcessesByName("WE9");
            if (ps.Length == 0) { Thread.Sleep(1000); continue; }
            h = Native.OpenProcess(Native.PROCESS_VM_READ | Native.PROCESS_QUERY_INFORMATION, false, ps[0].Id);
            if (h == IntPtr.Zero) { Console.WriteLine("No puedo abrir WE9 (ejecutar como administrador)"); Thread.Sleep(2000); continue; }
            W("=== conectado a WE9 pid " + ps[0].Id + " (duracion 0x3BE0F50=" + I32(0x3BE0F50) + ") ===");

            string prevFoul = null;
            string prevMatch = null; int prevBall = -1; int prevLast = -1, prevHold = -1, prevMode = -1;
            int prevN0 = -1, prevN1 = -1, prevFlag = 0; int contacto = 0; string contactoIds = "";
            byte[] pAcc = null, pToq = null; int pCon = -1; string pLineup = "";
            int[] ids = new int[23];
            int vistos = -1; HashSet<int> fichas = new HashSet<int>(); int[] pIds = null;
            int pSc0 = -1, pSc1 = -1;
            int[] pst = new int[23], psub = new int[23], pinp = new int[23];
            for (int i = 0; i < 23; i++) { pst[i] = -1; psub[i] = -1; }

            while (!ps[0].HasExited)
            {
                byte[] m = Read(0xFF078C, 10);
                if (m == null) { Thread.Sleep(50); continue; }
                string match = m[0] + "." + m[1] + "." + m[2] + "." + m[3];
                int ptr = I32(0xC20834);
                int ball = -1; float bx = 0, bz = 0, by = 0; int bv = 0;
                if (ptr != 0)
                {
                    byte[] bb = Read(ptr + 0x80, 2); if (bb != null) ball = BitConverter.ToUInt16(bb, 0);
                    bx = F32(ptr + 0x10); by = F32(ptr + 0x14); bz = F32(ptr + 0x18); bv = I32(ptr + 0x4c);
                }
                // historial de acciones de la pelota: pelota+0xC4..0xCD tipos (u8 x10, el mas nuevo primero), +0xCE..0xD7 jugadores (slot)
                if (ptr != 0)
                {
                    byte[] ac = Read(ptr + 0xC4, 20);
                    if (ac != null)
                    {
                        int k = Nuevos(pAcc, ac, 10, 10);
                        for (int e = k - 1; e >= 0; e--)
                            W("ACCION 0x" + ac[e].ToString("X2") + " " + Accion(ac[e]) + "  por " + Quien(ac[e + 10]));
                        pAcc = ac;
                    }
                }
                // historial de toques: 0x3BE0F82 = jugador con la pelota; 0x3BE0F83..0x3BE0F8C = ultimos 10 que la tocaron (slot, el mas nuevo primero)
                byte[] tq = Read(0x3BE0F82, 11);
                if (tq != null)
                {
                    byte[] t10 = new byte[10]; Array.Copy(tq, 1, t10, 0, 10);
                    int kt = Nuevos(pToq, t10, 10, 0);
                    for (int e = kt - 1; e >= 0; e--) W("TOQUE " + Quien(t10[e]));
                    pToq = t10;
                    if (tq[0] != pCon) { W("JUGADOR_CON_PELOTA (0x3BE0F82) " + pCon + " -> " + tq[0] + (tq[0] > 0 && tq[0] <= 22 ? " " + Quien(tq[0]) : "")); pCon = tq[0]; }
                }

                byte[] misc = Read(0x3BE0FE5, 1); int last = misc == null ? -1 : misc[0];
                byte[] hb = Read(0x3BE0FB8, 1); int hold = hb == null ? -1 : hb[0];
                byte[] mb = Read(0x3BE1A8C, 1); int mode = mb == null ? -1 : mb[0];

                // marcador y reloj (el contexto de cada linea del log)
                byte[] s0 = Read(0x3B86FA4, 2), s1 = Read(0x3B86FA4 + 0x744, 2);
                sc0 = s0 == null ? -1 : BitConverter.ToInt16(s0, 0);
                sc1 = s1 == null ? -1 : BitConverter.ToInt16(s1, 0);
                clk = I32(0x3BE0F74);

                // eventos ya identificados -> eventos_partido.txt
                if (sc0 != pSc0 || sc1 != pSc1)
                {
                    if (pSc0 >= 0) E("MARCADOR " + pSc0 + "-" + pSc1 + " -> " + sc0 + "-" + sc1);
                    pSc0 = sc0; pSc1 = sc1;
                }
                int n0 = m[0], n1 = m[1];
                if (n0 != prevN0 || n1 != prevN1)
                {
                    if (n0 == 3 && prevN0 != 3) E("REINICIO (nivel0=3, antes de un saque de centro)");
                    else if (n0 == 4 && n1 != prevN1)
                    {
                        string extra = "";
                        if (n1 == 6)
                        {
                            byte[] f6 = Read(0x3B8F800, 0x14);
                            if (f6 != null) extra = "  infractor=" + Quien(f6[9]) + " victima=" + Quien(f6[10]);
                        }
                        if (n1 == 1 && prevN1 > 1) E("REANUDA tras " + N1(prevN1) + " (" + match + ")");
                        else if (n1 != 1) E(N1(n1) + "  (" + match + ")" + extra);
                    }
                    prevN0 = n0; prevN1 = n1;
                }
                // contacto con registro de falta pero sin silbato (hipotesis, vuelta 85)
                byte[] ff = Read(0x3B8F800, 0x14);
                int flag = ff == null ? 0 : ff[1];
                if (flag == 1 && prevFlag == 0) { contacto = Environment.TickCount; contactoIds = ff == null ? "" : "  ids=" + Quien(ff[9]) + "/" + Quien(ff[10]); }
                prevFlag = flag;
                if (n1 == 6) contacto = 0;
                if (contacto != 0 && Environment.TickCount - contacto > 2000)
                {
                    E("CONTACTO_SIN_SILBATO [H]" + contactoIds);
                    contacto = 0;
                }

                bool changed = false;
                if (match != prevMatch) { W("PARTIDO nivel0.1.2.3 = " + (prevMatch ?? "?") + " -> " + match + "  (modo=" + mode + ")"); prevMatch = match; changed = true; }
                if (ball != prevBall) { W("PELOTA estado " + prevBall + " -> " + ball + string.Format("  X={0:F0} Z={1:F0} alt={2:F0} vel={3}", bx, bz, by, bv)); prevBall = ball; changed = true; }
                if (last != prevLast) { W("ULTIMA_ACCION (0x3BE0FE5) " + prevLast + " -> " + last); prevLast = last; }
                if (hold != prevHold) { W("POSEE_PELOTA (0x3BE0FB8, arquero) " + prevHold + " -> " + hold); prevHold = hold; }
                if (mode != prevMode) { W("MODO_JUEGO (0x3BE1A8C) " + prevMode + " -> " + mode); prevMode = mode; }

                // registro de FALTAS (ver re_notas.md vueltas 45-46): 0x3B8F800 tipo/sancion/demora/ids ; 0x3B8F820 registro de la infraccion
                byte[] fl = Read(0x3B8F800, 0x14);
                byte[] fr = Read(0x3B8F820, 0x14);
                string fls = (fl == null ? "?" : BitConverter.ToString(fl)) + " | " + (fr == null ? "?" : BitConverter.ToString(fr));
                if (fls != prevFoul)
                {
                    W("FALTA registro [tipo,sancion,..,demora(2B),..,ids] = " + fls);
                    prevFoul = fls;
                }

                for (int i = 0; i < 23; i++)
                {
                    long pb = 0x3BCF880L + i * 0x290L;
                    byte[] p = Read(pb, 0x110);
                    if (p == null) continue;
                    int st = p[0x42]; int sub = BitConverter.ToUInt16(p, 0x44);
                    ids[i] = BitConverter.ToUInt16(p, 0x3C);
                    slotEq[i] = p[0x40]; slotIdx[i] = p[0x3F];
                    // mascara de entrada (+0x108): direcciones 0x10/0x20/0x40/0x80 y botones desde 0x100; solo del jugador humano (+0x50 != 0)
                    int inp = BitConverter.ToInt32(p, 0x108);
                    if (p[0x50] != 0 && inp != pinp[i])
                    {
                        W(string.Format("INPUT jugador {0,2} eq{1} n{2,2} mascara 0x{3:X} -> 0x{4:X}  (estado 0x{5:X2})", i, p[0x40], p[0x3F], pinp[i], inp, st));
                    }
                    pinp[i] = inp;
                    if (st != pst[i] || sub != psub[i])
                    {
                        bool mostrar = todo || changed || !comunes.Contains(st) || !comunes.Contains(pst[i] < 0 ? 0 : pst[i]);
                        if (pst[i] >= 0 && mostrar)
                        {
                            int anim = I32(pb + 4); int mov = anim == 0 ? -1 : (ushort)(I32(anim + 0x30) & 0xFFFF);
                            W(string.Format("JUGADOR {0,2} eq{1} n{2,2} estado 0x{3:X2}/0x{4:X} -> 0x{5:X2}/0x{6:X}  mov=0x{7:X}  {8}", i, p[0x40], p[0x3F], pst[i], psub[i], st, sub, mov, NombreSlot(i)));
                        }
                        pst[i] = st; psub[i] = sub;
                    }
                }
                // lista de eventos del juego: cada registro nuevo se decodifica con nombres reales
                int lp = I32(0xFF694C);
                if (lp != 0)
                {
                    byte[] lb = Read((long)lp + 0x12EC, 0x2000);
                    if (lb != null)
                    {
                        List<byte[]> regs = Recorrer(lb);
                        if (vistos < 0) { vistos = regs.Count; if (vistos > 0) E("LISTA_EVENTOS previa al conectar: " + vistos + " registros (se ignoran)"); }
                        else if (regs.Count < vistos) { E("LISTA_EVENTOS reiniciada (partido nuevo)"); vistos = 0; }
                        for (; vistos < regs.Count; vistos++)
                        {
                            string d = DecEvento(regs[vistos]);
                            if (d != null) E("EVENTO_JUEGO " + d + "  crudo=" + BitConverter.ToString(regs[vistos]).Replace("-", ""));
                        }
                    }
                }
                // ficha de cada jugador la primera vez que aparece en cancha
                if (n0 == 4)
                    for (int i = 1; i <= 22; i++)
                        if (ids[i] > 0 && fichas.Add(ids[i])) E("FICHA id" + ids[i] + " " + NombreId(ids[i]) + ": " + Ficha(ids[i]));
                // alineacion en cancha: el orden de los bloques cambia con el lado (2o tiempo) y la formacion, por eso se registra cada vez que cambia
                if (n0 == 4)
                {
                    StringBuilder lu = new StringBuilder(); bool completo = true;
                    for (int i = 1; i <= 22; i++) { if (ids[i] <= 0) completo = false; lu.Append(ids[i]).Append(i == 11 ? " | " : " "); }
                    string lus = lu.ToString();
                    if (completo && lus != pLineup)
                    {
                        StringBuilder lun = new StringBuilder();
                        for (int i = 1; i <= 22; i++) lun.Append(i).Append("=").Append(NombreId(ids[i])).Append(i == 11 ? " | " : "; ");
                        E("ALINEACION (ids OF por bloque 1..22; el equipo de cada bloque va en el nombre) " + lus + "  -> " + lun.ToString());
                        // cambios de jugador: ids que entraron o salieron respecto de la alineacion anterior (un cambio de lado solo permuta ids)
                        if (pIds != null)
                        {
                            HashSet<int> antes = new HashSet<int>(), ahora = new HashSet<int>();
                            for (int i = 1; i <= 22; i++) { antes.Add(pIds[i]); ahora.Add(ids[i]); }
                            StringBuilder entra = new StringBuilder(), sale = new StringBuilder();
                            foreach (int x in ahora) if (!antes.Contains(x)) entra.Append(NombreId(x)).Append(" (eq").Append(EquipoDeId(x)).Append(") ");
                            foreach (int x in antes) if (!ahora.Contains(x)) sale.Append(NombreId(x)).Append(" ");
                            if (entra.Length > 0 || sale.Length > 0) E("CAMBIO DE JUGADOR entra: " + entra.ToString() + "| sale: " + sale.ToString());
                        }
                        pIds = (int[])ids.Clone();
                        pLineup = lus;
                    }
                }
                Thread.Sleep(15);
            }
            W("=== WE9 cerrado ===");
            Native.CloseHandle(h);
        }
    }
}
