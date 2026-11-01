// EscritorMemoria - escribe/congela valores en la memoria de WE9.exe EN EJECUCION (no toca ningun archivo del juego).
// Todo valor que se pisa se guarda la primera vez; "restaurar" lo devuelve. Al cerrar el juego, todo vuelve a la normalidad.
// Direcciones: ver PROPUESTAS_MODS.md / re_notas.md. Las marcadas [H] son hipotesis sin probar en el juego.
// Uso: EscritorMemoria.exe   (consola interactiva, escribir "ayuda")
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

static class Native
{
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    public const int VM_READ = 0x0010, VM_WRITE = 0x0020, VM_OPERATION = 0x0008, QUERY = 0x0400;
}

class Congelado { public long Dir; public byte[] Valor; public string Nombre; }

class Escritor
{
    static IntPtr h;
    static Process proc;
    static readonly object cerrojo = new object();
    static readonly Dictionary<long, byte[]> originales = new Dictionary<long, byte[]>();
    static readonly List<Congelado> congelados = new List<Congelado>();

    const long ENT_BASE = 0x3BCF880L, ENT_SIZE = 0x290L; const int ENT_N = 23;

    static byte[] Leer(long a, int n)
    {
        byte[] b = new byte[n]; int r;
        if (!Native.ReadProcessMemory(h, new IntPtr(a), b, n, out r) || r != n) return null;
        return b;
    }

    static bool Escribir(long a, byte[] v)
    {
        lock (cerrojo)
        {
            if (!originales.ContainsKey(a + v.Length - 1) && !originales.ContainsKey(a))
            {
                byte[] o = Leer(a, v.Length);
                if (o != null) originales[a] = o;
            }
            int w;
            return Native.WriteProcessMemory(h, new IntPtr(a), v, v.Length, out w) && w == v.Length;
        }
    }

    static byte[] Codificar(string tipo, string txt)
    {
        switch (tipo)
        {
            case "b": return new byte[] { (byte)ParseInt(txt) };
            case "w": return BitConverter.GetBytes((short)ParseInt(txt));
            case "d": return BitConverter.GetBytes((int)ParseInt(txt));
            case "f": return BitConverter.GetBytes(float.Parse(txt, CultureInfo.InvariantCulture));
        }
        throw new Exception("tipo invalido (b=byte w=short d=int f=float)");
    }

    static string Mostrar(string tipo, byte[] b)
    {
        switch (tipo)
        {
            case "b": return b[0] + " (0x" + b[0].ToString("X") + ")";
            case "w": return BitConverter.ToInt16(b, 0) + " (0x" + BitConverter.ToUInt16(b, 0).ToString("X") + ")";
            case "d": return BitConverter.ToInt32(b, 0) + " (0x" + BitConverter.ToUInt32(b, 0).ToString("X") + ")";
            default: return BitConverter.ToSingle(b, 0).ToString("R", CultureInfo.InvariantCulture);
        }
    }

    static int Largo(string tipo) { return tipo == "b" ? 1 : tipo == "w" ? 2 : 4; }

    static long ParseInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(s.Substring(2), 16);
        return long.Parse(s, CultureInfo.InvariantCulture);
    }

    static void Poner(string nombre, long dir, byte[] v, bool congelar)
    {
        if (!Escribir(dir, v)) { Console.WriteLine("  ERROR: no pude escribir en 0x" + dir.ToString("X")); return; }
        Console.WriteLine("  OK " + nombre + " @0x" + dir.ToString("X") + " = " + BitConverter.ToString(v) + (congelar ? " (congelado)" : ""));
        if (congelar)
            lock (cerrojo)
            {
                congelados.RemoveAll(c => c.Dir == dir);
                congelados.Add(new Congelado { Dir = dir, Valor = v, Nombre = nombre });
            }
    }

    static void Ayuda()
    {
        Console.WriteLine(@"
Comandos (numeros en decimal o 0x..; tipo: b=byte w=short d=int f=float):
  ver <dir> [tipo=d] [n=1]          leer memoria
  esc <dir> <valor> [tipo=d]        escribir una vez
  cong <dir> <valor> [tipo=d]       escribir y mantener (re-escribe cada 30 ms)
  descong [dir|todo]                dejar de mantener
  lista                             mostrar lo congelado y lo guardado para restaurar
  restaurar                         devolver TODO a los valores originales (y descongelar)

Atajos (confianza C = confirmada por codigo, H = hipotesis):
  dificultad <0..5>                 byte 0x3BE0F4C                       [C]
  goles <local> <visitante>         shorts 0x3B86FA4 / 0x3B876E8         [C]
  tiempo <frames>                   int 0x3BE0F74 (frames restantes)     [H tipo]
  duracion <opc>                    byte 0x3BE0F50 (cada tiempo (opc+1)*150 s; usar ANTES del partido) [C]
  lesiones <0|1>                    byte 0x3BE0F4B (usar ANTES del partido; 0 = sin lesiones) [C]
  pase <jugador 0..22|todos> <0|1|2> ent+0x241: 0 corto x0.8, 1 normal, 2 largo x1.2 (se congela) [C uso]
  pelota <x> <y> <z>                posicion de la pelota [[0xC20834]]+0x10/0x14/0x18 (float; X, alt, Z) [C]
  jugador <0..22> <x> <z>           ent+0x120 / +0x128 (float)         [C posiciones, ver nota 22]
  arrastre <float>                  0x3BE1A78 (4551266.0 original)      [H]
  magnus <float>                    0x3BE1A80 (1400000.0 original)      [H]
  friccion <float>                  0x3BE1A84 (160.0 original)          [H]
  salir
Ejemplos:  dificultad 5   |   goles 3 0   |   pase todos 2   |   cong 0x3BE0F74 100000 d   |   restaurar");
    }

    static long Ent(string s)
    {
        int i = (int)ParseInt(s);
        if (i < 0 || i >= ENT_N) throw new Exception("jugador fuera de 0..22");
        return ENT_BASE + i * ENT_SIZE;
    }

    static void Ejecutar(string linea)
    {
        string[] t = linea.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return;
        string c = t[0].ToLowerInvariant();
        switch (c)
        {
            case "ayuda": case "help": case "?": Ayuda(); break;
            case "ver":
            {
                long d = ParseInt(t[1]); string ty = t.Length > 2 ? t[2] : "d"; int n = t.Length > 3 ? (int)ParseInt(t[3]) : 1;
                for (int i = 0; i < n; i++)
                {
                    byte[] b = Leer(d + i * Largo(ty), Largo(ty));
                    Console.WriteLine("  0x" + (d + i * Largo(ty)).ToString("X") + " = " + (b == null ? "(ilegible)" : Mostrar(ty, b)));
                }
                break;
            }
            case "esc": case "cong":
            {
                string ty = t.Length > 3 ? t[3] : "d";
                Poner("manual", ParseInt(t[1]), Codificar(ty, t[2]), c == "cong");
                break;
            }
            case "descong":
                lock (cerrojo)
                {
                    if (t.Length < 2 || t[1] == "todo") congelados.Clear();
                    else { long d = ParseInt(t[1]); congelados.RemoveAll(x => x.Dir == d); }
                }
                Console.WriteLine("  ok"); break;
            case "lista":
                lock (cerrojo)
                {
                    Console.WriteLine("  congelados: " + congelados.Count);
                    foreach (Congelado x in congelados) Console.WriteLine("    0x" + x.Dir.ToString("X") + " " + x.Nombre + " = " + BitConverter.ToString(x.Valor));
                    Console.WriteLine("  guardados para restaurar: " + originales.Count);
                }
                break;
            case "restaurar":
                lock (cerrojo)
                {
                    congelados.Clear();
                    int ok = 0;
                    foreach (KeyValuePair<long, byte[]> kv in originales)
                    {
                        int w; if (Native.WriteProcessMemory(h, new IntPtr(kv.Key), kv.Value, kv.Value.Length, out w)) ok++;
                    }
                    Console.WriteLine("  restaurados " + ok + " de " + originales.Count);
                    originales.Clear();
                }
                break;
            case "dificultad":
            {
                long v = ParseInt(t[1]); if (v < 0 || v > 5) throw new Exception("0..5");
                Poner("dificultad", 0x3BE0F4C, new byte[] { (byte)v }, false); break;
            }
            case "goles":
                Poner("goles local", 0x3B86FA4, BitConverter.GetBytes((short)ParseInt(t[1])), false);
                Poner("goles visitante", 0x3B876E8, BitConverter.GetBytes((short)ParseInt(t[2])), false); break;
            case "tiempo": Poner("tiempo restante", 0x3BE0F74, BitConverter.GetBytes((int)ParseInt(t[1])), false); break;
            case "duracion": Poner("duracion", 0x3BE0F50, new byte[] { (byte)ParseInt(t[1]) }, false); break;
            case "lesiones": Poner("lesiones", 0x3BE0F4B, new byte[] { (byte)ParseInt(t[1]) }, false); break;
            case "pase":
            {
                long v = ParseInt(t[2]); if (v < 0 || v > 2) throw new Exception("0, 1 o 2");
                if (t[1].ToLowerInvariant() == "todos")
                    for (int i = 0; i < ENT_N; i++) Poner("pase j" + i, ENT_BASE + i * ENT_SIZE + 0x241, new byte[] { (byte)v }, true);
                else Poner("pase j" + t[1], Ent(t[1]) + 0x241, new byte[] { (byte)v }, true);
                break;
            }
            case "pelota":
            {
                byte[] p = Leer(0xC20834, 4); long bp = p == null ? 0 : BitConverter.ToInt32(p, 0);
                if (bp == 0) throw new Exception("no hay pelota (fuera de partido?)");
                Poner("pelota X", bp + 0x10, Codificar("f", t[1]), false);
                Poner("pelota alt", bp + 0x14, Codificar("f", t[2]), false);
                Poner("pelota Z", bp + 0x18, Codificar("f", t[3]), false); break;
            }
            case "jugador":
                Poner("jugador X", Ent(t[1]) + 0x120, Codificar("f", t[2]), false);
                Poner("jugador Z", Ent(t[1]) + 0x128, Codificar("f", t[3]), false); break;
            case "arrastre": Poner("arrastre", 0x3BE1A78, Codificar("f", t[1]), true); break;
            case "magnus": Poner("magnus", 0x3BE1A80, Codificar("f", t[1]), true); break;
            case "friccion": Poner("friccion", 0x3BE1A84, Codificar("f", t[1]), true); break;
            default: Console.WriteLine("  comando desconocido; escribi 'ayuda'"); break;
        }
    }

    static void Main()
    {
        Console.WriteLine("EscritorMemoria - esperando WE9.exe ... (Ctrl+C para salir)");
        while (true)
        {
            Process[] ps = Process.GetProcessesByName("WE9");
            if (ps.Length > 0)
            {
                proc = ps[0];
                h = Native.OpenProcess(Native.VM_READ | Native.VM_WRITE | Native.VM_OPERATION | Native.QUERY, false, proc.Id);
                if (h != IntPtr.Zero) break;
                Console.WriteLine("No puedo abrir WE9 (ejecutar como administrador)");
            }
            Thread.Sleep(1000);
        }
        Console.WriteLine("Conectado a WE9 pid " + proc.Id + ". Escribi 'ayuda'.");
        Console.WriteLine("AVISO: modifica la memoria del juego en vivo; cerrar el juego descarta todo. Hipotesis [H] sin probar.");

        Thread hilo = new Thread(delegate ()
        {
            while (true)
            {
                lock (cerrojo)
                    foreach (Congelado c in congelados)
                    {
                        int w; Native.WriteProcessMemory(h, new IntPtr(c.Dir), c.Valor, c.Valor.Length, out w);
                    }
                Thread.Sleep(30);
            }
        });
        hilo.IsBackground = true; hilo.Start();

        while (!proc.HasExited)
        {
            Console.Write("> ");
            string l = Console.ReadLine();
            if (l == null) break;
            if (l.Trim().ToLowerInvariant() == "salir") break;
            try { Ejecutar(l); }
            catch (IndexOutOfRangeException) { Console.WriteLine("  faltan argumentos; 'ayuda'"); }
            catch (Exception e) { Console.WriteLine("  error: " + e.Message); }
        }
        if (proc.HasExited) Console.WriteLine("WE9 cerrado.");
        Native.CloseHandle(h);
    }
}
