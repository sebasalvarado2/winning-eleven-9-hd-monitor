// WE9 Monitor: abre el juego, lee su memoria en tiempo real, escribe un log
// y muestra avisos/logros superpuestos sobre la ventana del juego.
// Incluye escáner de memoria, visor hexadecimal y panel de habilidades.
// Las direcciones a vigilar se configuran en watches.txt y habilidades.txt.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace WE9Monitor
{
    static class Native
    {
        public const int PROCESS_VM_OPERATION = 0x08;
        public const int PROCESS_VM_READ = 0x10;
        public const int PROCESS_VM_WRITE = 0x20;
        public const int PROCESS_QUERY_INFORMATION = 0x400;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);

        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory")]
        public static extern bool ReadProcessMemoryPtr(IntPtr h, IntPtr addr, IntPtr buf, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll")]
        public static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, IntPtr len);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EnumProcessModulesEx(IntPtr h, [Out] IntPtr[] mods, int cb, out int needed, int filter);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr h, out RECT r);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr h, ref POINT p);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr h, int id);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
    }

    class MemRegion
    {
        public long Base;
        public int Size;
    }

    // Acceso a la memoria del proceso del juego.
    class GameMemory
    {
        public Process Proc;
        public long ModuleBase = 0x400000;
        public long ModuleSize;
        public bool CanWrite;
        IntPtr handle = IntPtr.Zero;

        public bool Attach(Process p)
        {
            handle = Native.OpenProcess(Native.PROCESS_VM_READ | Native.PROCESS_QUERY_INFORMATION |
                                        Native.PROCESS_VM_WRITE | Native.PROCESS_VM_OPERATION, false, p.Id);
            CanWrite = handle != IntPtr.Zero;
            if (!CanWrite) handle = Native.OpenProcess(Native.PROCESS_VM_READ | Native.PROCESS_QUERY_INFORMATION, false, p.Id);
            if (handle == IntPtr.Zero) return false;
            Proc = p;

            IntPtr[] mods = new IntPtr[1];
            int needed;
            if (Native.EnumProcessModulesEx(handle, mods, IntPtr.Size, out needed, 0x01) && mods[0] != IntPtr.Zero)
                ModuleBase = mods[0].ToInt64();
            byte[] lfanew = Read(ModuleBase + 0x3C, 4);
            if (lfanew != null)
            {
                byte[] size = Read(ModuleBase + BitConverter.ToInt32(lfanew, 0) + 0x50, 4);
                if (size != null) ModuleSize = BitConverter.ToUInt32(size, 0);
            }
            return true;
        }

        public void Detach()
        {
            if (handle != IntPtr.Zero) Native.CloseHandle(handle);
            handle = IntPtr.Zero;
            Proc = null;
        }

        public bool Alive
        {
            get { return handle != IntPtr.Zero && Read(ModuleBase, 2) != null; }
        }

        public byte[] Read(long addr, int size)
        {
            if (handle == IntPtr.Zero || addr <= 0) return null;
            byte[] b = new byte[size];
            int read;
            if (!Native.ReadProcessMemory(handle, new IntPtr(addr), b, size, out read) || read != size) return null;
            return b;
        }

        // Lee directo a un buffer ya fijado en memoria (sin crear arrays nuevos).
        public bool ReadInto(long addr, IntPtr dest, int size)
        {
            if (handle == IntPtr.Zero) return false;
            IntPtr read;
            return Native.ReadProcessMemoryPtr(handle, new IntPtr(addr), dest, new IntPtr(size), out read) && read.ToInt64() == size;
        }

        // Rango de las secciones de datos escribibles de WE9.exe (.data y similares).
        public bool DataSectionRange(out long start, out long end)
        {
            start = end = 0;
            byte[] hdr = Read(ModuleBase, 0x1000);
            if (hdr == null) return false;
            int pe = BitConverter.ToInt32(hdr, 0x3C);
            int n = BitConverter.ToUInt16(hdr, pe + 6);
            int sec = pe + 24 + BitConverter.ToUInt16(hdr, pe + 20);
            for (int i = 0; i < n; i++)
            {
                int o = sec + 40 * i;
                if (o + 40 > hdr.Length) break;
                uint flags = BitConverter.ToUInt32(hdr, o + 36);
                if ((flags & 0x80000000) == 0) continue; // no escribible
                long va = ModuleBase + BitConverter.ToUInt32(hdr, o + 12);
                long vs = BitConverter.ToUInt32(hdr, o + 8);
                if (start == 0 || va < start) start = va;
                if (va + vs > end) end = va + vs;
            }
            start &= ~0xFFFL;
            end = (end + 0xFFF) & ~0xFFFL;
            return start != 0;
        }

        public bool Write(long addr, byte[] data)
        {
            if (!CanWrite || handle == IntPtr.Zero) return false;
            int written;
            return Native.WriteProcessMemory(handle, new IntPtr(addr), data, data.Length, out written) && written == data.Length;
        }

        // Escribe en memoria protegida (por ejemplo la zona de código), restaurando la protección después.
        public bool WriteProtected(long addr, byte[] data)
        {
            if (!CanWrite || handle == IntPtr.Zero) return false;
            uint old;
            if (!Native.VirtualProtectEx(handle, new IntPtr(addr), new UIntPtr((uint)data.Length), 0x40, out old)) return false;
            bool ok = Write(addr, data);
            uint dummy;
            Native.VirtualProtectEx(handle, new IntPtr(addr), new UIntPtr((uint)data.Length), old, out dummy);
            return ok;
        }

        // Regiones de memoria escribibles: ahí viven los datos del partido y de los jugadores.
        public List<MemRegion> WritableRegions()
        {
            List<MemRegion> list = new List<MemRegion>();
            IntPtr len = new IntPtr(Marshal.SizeOf(typeof(Native.MEMORY_BASIC_INFORMATION)));
            const uint writable = 0x04 | 0x08 | 0x40 | 0x80;
            long addr = 0x10000;
            while (addr < 0x100000000L)
            {
                Native.MEMORY_BASIC_INFORMATION mbi;
                if (Native.VirtualQueryEx(handle, new IntPtr(addr), out mbi, len) == IntPtr.Zero) break;
                long rbase = mbi.BaseAddress.ToInt64();
                long rsize = mbi.RegionSize.ToInt64();
                if (rsize <= 0) break;
                if (mbi.State == 0x1000 && (mbi.Protect & writable) != 0 && (mbi.Protect & 0x100) == 0 && rsize < int.MaxValue)
                    list.Add(new MemRegion { Base = rbase, Size = (int)rsize });
                addr = rbase + rsize;
            }
            return list;
        }

        // "WE9.exe+XXXX" si la dirección es fija dentro del ejecutable, si no la absoluta.
        public string Describe(long addr)
        {
            if (ModuleSize > 0 && addr >= ModuleBase && addr < ModuleBase + ModuleSize)
                return "WE9.exe+" + (addr - ModuleBase).ToString("X");
            return addr.ToString("X8");
        }

        // Expresion: "WE9.exe+3BA000, 10, 4" -> lee puntero en base, suma 0x10, lee puntero, suma 4.
        // Tambien acepta una direccion fija: "0x0123ABCD".
        public bool Resolve(string expr, out long addr)
        {
            addr = 0;
            string[] parts = expr.Split(',');
            string first = parts[0].Trim();
            int plus = first.IndexOf('+');
            if (plus >= 0)
            {
                string left = first.Substring(0, plus).Trim();
                long off;
                if (!ParseHex(first.Substring(plus + 1), out off)) return false;
                long baseAddr;
                if (left.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) baseAddr = ModuleBase;
                else if (!ParseHex(left, out baseAddr)) return false;
                addr = baseAddr + off;
            }
            else if (!ParseHex(first, out addr)) return false;

            for (int i = 1; i < parts.Length; i++)
            {
                byte[] p = Read(addr, 4);
                if (p == null) return false;
                long off;
                if (!ParseHex(parts[i], out off)) return false;
                long ptr = BitConverter.ToUInt32(p, 0);
                if (ptr == 0) return false;
                addr = ptr + off;
            }
            return true;
        }

        public static bool ParseHex(string s, out long v)
        {
            s = s.Trim();
            bool neg = s.StartsWith("-");
            if (neg) s = s.Substring(1).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            bool ok = long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            if (neg) v = -v;
            return ok;
        }
    }

    static class Values
    {
        public static int SizeOf(string type)
        {
            switch (type)
            {
                case "byte": return 1;
                case "short": return 2;
                default: return 4;
            }
        }

        public static double Decode(string type, byte[] b, int i)
        {
            switch (type)
            {
                case "byte": return b[i];
                case "short": return BitConverter.ToInt16(b, i);
                case "float": return BitConverter.ToSingle(b, i);
                default: return BitConverter.ToInt32(b, i);
            }
        }

        public static string Format(string type, double v)
        {
            return type == "float" ? v.ToString("0.###", CultureInfo.InvariantCulture) : ((long)v).ToString();
        }
    }

    // Una linea de watches.txt.
    class Watch
    {
        public string Name, Expr, Type, Condition, Message, Mode;
        public bool HasLast;
        public double Last;
        public long ResolvedAddr;
        public bool Readable = true;
        public string Display = "-";

        public bool Triggers(double prev, double now)
        {
            if (Condition == "cambia") return true;
            if (Condition == "aumenta") return now > prev;
            if (Condition == "disminuye") return now < prev;
            if (Condition.StartsWith("="))
            {
                double target;
                if (double.TryParse(Condition.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                    return now == target && prev != target;
            }
            return false;
        }

        public static List<Watch> Load(string path, Action<string> log)
        {
            List<Watch> list = new List<Watch>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] c = line.Split('|');
                if (c.Length < 6)
                {
                    log("watches.txt linea " + (i + 1) + ": faltan columnas (son 6 separadas por |)");
                    continue;
                }
                Watch w = new Watch();
                w.Name = c[0].Trim();
                w.Expr = c[1].Trim();
                w.Type = c[2].Trim().ToLowerInvariant();
                w.Condition = c[3].Trim().ToLowerInvariant().Replace(" ", "");
                w.Message = c[4].Trim();
                w.Mode = c[5].Trim().ToLowerInvariant();
                list.Add(w);
            }
            return list;
        }
    }

    // Una linea de habilidades.txt: un bit dentro del registro de un jugador.
    class Skill
    {
        public string Name;
        public int Offset, Bit;
        public bool HasState, State;

        public static List<Skill> Load(string path, Action<string> log)
        {
            List<Skill> list = new List<Skill>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] c = line.Split('|');
                long off, bit;
                if (c.Length < 3 || !GameMemory.ParseHex(c[1], out off) || !long.TryParse(c[2].Trim(), out bit) || bit < 0 || bit > 7)
                {
                    log("habilidades.txt linea " + (i + 1) + ": formato inválido (nombre | offset hex | bit 0-7)");
                    continue;
                }
                Skill s = new Skill();
                s.Name = c[0].Trim();
                s.Offset = (int)off;
                s.Bit = (int)bit;
                list.Add(s);
            }
            return list;
        }
    }

    // Ventana transparente, siempre encima y que deja pasar los clics al juego.
    class OverlayForm : Form
    {
        class Toast
        {
            public string Title, Text;
            public bool Gold;
            public DateTime Start;
        }

        const double Duration = 3.5;
        readonly List<Toast> toasts = new List<Toast>();
        readonly Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        readonly Font textFont = new Font("Segoe UI", 14f, FontStyle.Bold);

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void AddToast(string title, string text, bool gold)
        {
            Toast t = new Toast();
            t.Title = title;
            t.Text = text;
            t.Gold = gold;
            t.Start = DateTime.Now;
            toasts.Add(t);
            if (toasts.Count > 5) toasts.RemoveAt(0);
            Invalidate();
        }

        public void Tick()
        {
            if (toasts.Count == 0) return;
            toasts.RemoveAll(t => (DateTime.Now - t.Start).TotalSeconds > Duration);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = 340, h = 62, margin = 16, y = margin;
            foreach (Toast t in toasts)
            {
                double age = (DateTime.Now - t.Start).TotalSeconds;
                double slide = Math.Min(1.0, age / 0.2);
                if (age > Duration - 0.2) slide = Math.Max(0.0, (Duration - age) / 0.2);
                int x = ClientSize.Width - margin - (int)((w + margin) * slide) + margin;

                Color accent = t.Gold ? Color.FromArgb(240, 190, 40) : Color.FromArgb(60, 160, 255);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 26, 34)))
                    g.FillRectangle(bg, x, y, w, h);
                using (SolidBrush bar = new SolidBrush(accent))
                    g.FillRectangle(bar, x, y, 6, h);
                using (SolidBrush tb = new SolidBrush(accent))
                    g.DrawString(t.Title, titleFont, tb, x + 16, y + 7);
                g.DrawString(t.Text, textFont, Brushes.White, x + 16, y + 26);
                y += h + 8;
            }
        }
    }

    // Visor hexadecimal en vivo: resalta en rojo los bytes que cambian y en
    // amarillo los que difieren de la copia guardada (A).
    class HexView : Control
    {
        public long Address;
        public int Selected = -1;
        public byte[] Compare;
        public long CompareStart;
        public event EventHandler SelectionChanged;
        public event EventHandler AddressChanged;

        byte[] data = new byte[0];
        bool[] readable = new bool[0];
        DateTime[] changed = new DateTime[0];
        long dataAddress = -1;
        readonly Font font = new Font("Consolas", 10f);
        readonly int cw, rh;
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public HexView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 28);
            cw = TextRenderer.MeasureText("0000000000000000", font, Size.Empty, Flags).Width / 16;
            rh = font.Height + 2;
        }

        public int Rows
        {
            get { return Math.Max(1, (ClientSize.Height - 4) / rh); }
        }

        public int ByteCount
        {
            get { return Rows * 16; }
        }

        public void Reload(GameMemory mem)
        {
            int n = ByteCount;
            byte[] nd = new byte[n];
            bool[] nr = new bool[n];
            DateTime[] nc = new DateTime[n];
            bool same = dataAddress == Address;
            for (int row = 0; row < n; row += 16)
            {
                byte[] b = mem.Read(Address + row, 16);
                if (b == null) continue;
                for (int j = 0; j < 16; j++)
                {
                    int i = row + j;
                    nd[i] = b[j];
                    nr[i] = true;
                    if (same && i < data.Length)
                    {
                        nc[i] = changed[i];
                        if (readable[i] && data[i] != b[j]) nc[i] = DateTime.Now;
                    }
                }
            }
            data = nd;
            readable = nr;
            changed = nc;
            dataAddress = Address;
            Invalidate();
        }

        int ByteX(int j)
        {
            return 4 + 10 * cw + (j * 3 + (j >= 8 ? 1 : 0)) * cw;
        }

        int AsciiX
        {
            get { return ByteX(16) + 2 * cw; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Color normal = Color.FromArgb(210, 210, 210);
            Color dim = Color.FromArgb(95, 95, 95);
            for (int row = 0; row < Rows; row++)
            {
                int y = 2 + row * rh;
                TextRenderer.DrawText(g, (Address + row * 16).ToString("X8"), font, new Point(4, y), Color.FromArgb(110, 140, 180), Flags);
                for (int j = 0; j < 16; j++)
                {
                    int i = row * 16 + j;
                    int x = ByteX(j);
                    string s = "??";
                    char ch = ' ';
                    Color fg = dim;
                    if (i < data.Length && readable[i])
                    {
                        byte b = data[i];
                        s = b.ToString("X2");
                        ch = (b >= 32 && b < 127) ? (char)b : '.';
                        fg = b == 0 ? dim : normal;

                        long k = Address + i - CompareStart;
                        if (Compare != null && k >= 0 && k < Compare.Length && Compare[k] != b)
                        {
                            using (SolidBrush br = new SolidBrush(Color.FromArgb(80, 65, 10)))
                                g.FillRectangle(br, x - 2, y, 2 * cw + 4, rh);
                            fg = Color.FromArgb(255, 215, 70);
                        }
                        double age = (DateTime.Now - changed[i]).TotalSeconds;
                        if (age < 1.5)
                        {
                            int a = (int)(170 * (1 - age / 1.5));
                            using (SolidBrush br = new SolidBrush(Color.FromArgb(a, 220, 40, 40)))
                                g.FillRectangle(br, x - 2, y, 2 * cw + 4, rh);
                        }
                    }
                    if (i == Selected)
                    {
                        using (Pen p = new Pen(Color.FromArgb(80, 170, 255), 2))
                        {
                            g.DrawRectangle(p, x - 2, y, 2 * cw + 4, rh - 1);
                            g.DrawRectangle(p, AsciiX + j * cw - 1, y, cw + 1, rh - 1);
                        }
                        fg = Color.White;
                    }
                    TextRenderer.DrawText(g, s, font, new Point(x, y), fg, Flags);
                    TextRenderer.DrawText(g, ch.ToString(), font, new Point(AsciiX + j * cw, y), i == Selected ? Color.White : Color.FromArgb(150, 150, 150), Flags);
                }
            }
        }

        public void SelectByte(int i)
        {
            if (i < 0)
            {
                Address = Math.Max(0, Address - 16);
                i += 16;
                if (AddressChanged != null) AddressChanged(this, EventArgs.Empty);
            }
            else if (i >= ByteCount)
            {
                Address += 16;
                i -= 16;
                if (AddressChanged != null) AddressChanged(this, EventArgs.Empty);
            }
            Selected = i;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        void Scroll(long delta)
        {
            Address = Math.Max(0, Address + delta);
            Invalidate();
            if (AddressChanged != null) AddressChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int row = (e.Y - 2) / rh;
            if (row < 0 || row >= Rows) return;
            for (int j = 0; j < 16; j++)
            {
                int x = ByteX(j);
                bool hexHit = e.X >= x - cw / 2 && e.X < x + 2 * cw + cw / 2;
                bool asciiHit = e.X >= AsciiX + j * cw && e.X < AsciiX + (j + 1) * cw;
                if (hexHit || asciiHit)
                {
                    SelectByte(row * 16 + j);
                    return;
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Scroll(-(e.Delta / 120) * 0x40);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int sel = Math.Max(0, Selected);
            switch (e.KeyCode)
            {
                case Keys.Left: SelectByte(sel - 1); break;
                case Keys.Right: SelectByte(sel + 1); break;
                case Keys.Up: SelectByte(sel - 16); break;
                case Keys.Down: SelectByte(sel + 16); break;
                case Keys.PageUp: Scroll(-ByteCount); break;
                case Keys.PageDown: Scroll(ByteCount); break;
            }
        }
    }

    class HexTab : UserControl
    {
        readonly MainForm main;
        public readonly HexView View = new HexView();
        readonly TextBox addrBox = new TextBox();
        readonly TextBox writeBox = new TextBox();
        readonly Label info = new Label();
        readonly CheckBox[] bits = new CheckBox[8];
        bool updatingBits;
        byte[] snapshotA;

        public HexTab(MainForm main)
        {
            this.main = main;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 34;
            bar.Padding = new Padding(2);
            Label l = new Label();
            l.Text = "Dirección:";
            l.AutoSize = true;
            l.Margin = new Padding(4, 8, 0, 0);
            bar.Controls.Add(l);
            addrBox.Width = 200;
            addrBox.Margin = new Padding(4, 5, 4, 0);
            addrBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { GoToText(); e.SuppressKeyPress = true; } };
            bar.Controls.Add(addrBox);
            bar.Controls.Add(MainForm.MakeButton("Ir", delegate { GoToText(); }));
            bar.Controls.Add(MainForm.MakeButton("-0x100", delegate { GoTo(View.Address - 0x100); }));
            bar.Controls.Add(MainForm.MakeButton("+0x100", delegate { GoTo(View.Address + 0x100); }));
            bar.Controls.Add(MainForm.MakeButton("Guardar A", delegate { SaveA(); }));
            bar.Controls.Add(MainForm.MakeButton("Comparar con A", delegate { CompareA(); }));
            bar.Controls.Add(MainForm.MakeButton("Quitar comparación", delegate { View.Compare = null; }));
            bar.Controls.Add(MainForm.MakeButton("Diferencias al log", delegate { DiffToLog(); }));

            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 290;
            side.Padding = new Padding(8);

            info.Dock = DockStyle.Top;
            info.Height = 130;
            info.Font = new Font("Consolas", 9f);

            FlowLayoutPanel bitPanel = new FlowLayoutPanel();
            bitPanel.Dock = DockStyle.Top;
            bitPanel.Height = 48;
            Label bl = new Label();
            bl.Text = "Bits (clic = activar/desactivar):";
            bl.AutoSize = true;
            bitPanel.SetFlowBreak(bl, true);
            bitPanel.Controls.Add(bl);
            for (int k = 0; k < 8; k++)
            {
                int bit = 7 - k;
                CheckBox cb = new CheckBox();
                cb.Text = bit.ToString();
                cb.AutoSize = true;
                cb.Margin = new Padding(0, 2, 2, 0);
                cb.CheckedChanged += delegate { OnBitChanged(bit, cb.Checked); };
                bits[k] = cb;
                bitPanel.Controls.Add(cb);
            }

            FlowLayoutPanel writePanel = new FlowLayoutPanel();
            writePanel.Dock = DockStyle.Top;
            writePanel.Height = 34;
            writeBox.Width = 140;
            writeBox.Margin = new Padding(0, 5, 4, 0);
            writePanel.Controls.Add(writeBox);
            writePanel.Controls.Add(MainForm.MakeButton("Escribir bytes", delegate { WriteBytes(); }));

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.Height = 70;
            actions.Controls.Add(MainForm.MakeButton("Vigilar este byte", delegate { WatchSelected(); }));
            actions.Controls.Add(MainForm.MakeButton("Usar como inicio de jugador", delegate { UseAsPlayer(); }));

            Label help = new Label();
            help.Dock = DockStyle.Fill;
            help.ForeColor = Color.DimGray;
            help.Text = "Rojo: byte que acaba de cambiar.\nAmarillo: distinto de la copia A.\n\n" +
                        "Para encontrar la habilidad de un jugador: pon la vista al inicio del jugador A, " +
                        "\"Guardar A\"; ve al inicio del jugador B, \"Comparar con A\". " +
                        "Rueda del ratón o flechas para moverte.";

            side.Controls.Add(help);
            side.Controls.Add(actions);
            side.Controls.Add(writePanel);
            side.Controls.Add(bitPanel);
            side.Controls.Add(info);

            View.Dock = DockStyle.Fill;
            View.AddressChanged += delegate { addrBox.Text = main.Mem.Describe(View.Address); };
            View.SelectionChanged += delegate { UpdateInspector(); };

            Controls.Add(View);
            Controls.Add(side);
            Controls.Add(bar);
        }

        long SelectedAddress
        {
            get { return View.Address + Math.Max(0, View.Selected); }
        }

        public void GoTo(long addr)
        {
            View.Address = Math.Max(0, addr);
            addrBox.Text = main.Mem.Describe(View.Address);
            if (main.Attached) View.Reload(main.Mem);
        }

        void GoToText()
        {
            long a;
            if (main.Mem.Resolve(addrBox.Text, out a)) GoTo(a);
            else main.Log("Hex: dirección inválida o no legible: " + addrBox.Text);
        }

        public void Tick()
        {
            if (!main.Attached) return;
            View.Reload(main.Mem);
            UpdateInspector();
        }

        void UpdateInspector()
        {
            GameMemory mem = main.Mem;
            if (View.Selected < 0 || !main.Attached)
            {
                SetInfo("Haz clic en un byte para ver sus valores.");
                SetBitsEnabled(false);
                return;
            }
            long addr = SelectedAddress;
            byte[] b4 = mem.Read(addr, 4);
            byte[] b1 = b4 ?? mem.Read(addr, 1);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Dirección  " + mem.Describe(addr));
            sb.AppendLine("Absoluta   " + addr.ToString("X8"));
            sb.AppendLine("Desde vista +0x" + View.Selected.ToString("X"));
            if (b1 == null)
            {
                sb.AppendLine("No se puede leer");
                SetInfo(sb.ToString());
                SetBitsEnabled(false);
                return;
            }
            byte v = b1[0];
            sb.AppendLine("Byte       0x" + v.ToString("X2") + " = " + v);
            if (b4 != null)
            {
                sb.AppendLine("2 bytes    " + BitConverter.ToInt16(b4, 0));
                sb.AppendLine("4 bytes    " + BitConverter.ToInt32(b4, 0) + "  (0x" + BitConverter.ToUInt32(b4, 0).ToString("X8") + ")");
                sb.AppendLine("Float      " + BitConverter.ToSingle(b4, 0).ToString("0.####", CultureInfo.InvariantCulture));
            }
            SetInfo(sb.ToString());

            updatingBits = true;
            for (int k = 0; k < 8; k++) bits[k].Checked = ((v >> (7 - k)) & 1) == 1;
            updatingBits = false;
            SetBitsEnabled(mem.CanWrite);
        }

        void SetInfo(string text)
        {
            if (info.Text != text) info.Text = text;
        }

        void SetBitsEnabled(bool on)
        {
            foreach (CheckBox cb in bits)
                if (cb.Enabled != on) cb.Enabled = on;
        }

        void OnBitChanged(int bit, bool on)
        {
            if (updatingBits || View.Selected < 0 || !main.Attached) return;
            long addr = SelectedAddress;
            byte[] b = main.Mem.Read(addr, 1);
            if (b == null) return;
            byte nv = on ? (byte)(b[0] | (1 << bit)) : (byte)(b[0] & ~(1 << bit));
            if (main.Mem.Write(addr, new byte[] { nv }))
                main.Log("Hex: " + main.Mem.Describe(addr) + " bit " + bit + (on ? " activado" : " desactivado") +
                         " (0x" + b[0].ToString("X2") + " -> 0x" + nv.ToString("X2") + ")");
            else
                main.Log("Hex: no se pudo escribir en " + main.Mem.Describe(addr));
        }

        void WriteBytes()
        {
            if (View.Selected < 0 || !main.Attached) return;
            string hex = writeBox.Text.Replace(" ", "").Replace("0x", "");
            if (hex.Length == 0 || hex.Length % 2 != 0)
            {
                main.Log("Hex: escribe bytes en hexadecimal, por ejemplo: 08 o 08 FF 1A");
                return;
            }
            byte[] data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[i]))
                {
                    main.Log("Hex: valor inválido: " + writeBox.Text);
                    return;
                }
            }
            long addr = SelectedAddress;
            byte[] old = main.Mem.Read(addr, data.Length);
            if (main.Mem.Write(addr, data))
                main.Log("Hex: escrito en " + main.Mem.Describe(addr) + ": " +
                         (old != null ? BitConverter.ToString(old).Replace("-", " ") : "??") + " -> " + BitConverter.ToString(data).Replace("-", " "));
            else
                main.Log("Hex: no se pudo escribir en " + main.Mem.Describe(addr));
        }

        void SaveA()
        {
            if (!main.Attached) return;
            byte[] a = main.Mem.Read(View.Address, 0x400);
            if (a == null)
            {
                main.Log("Hex: no se pudo leer la vista para guardar A");
                return;
            }
            snapshotA = a;
            main.Log("Hex: copia A guardada desde " + main.Mem.Describe(View.Address) + " (0x400 bytes)");
        }

        void CompareA()
        {
            if (snapshotA == null)
            {
                main.Log("Hex: primero pulsa \"Guardar A\"");
                return;
            }
            View.Compare = snapshotA;
            View.CompareStart = View.Address;
            main.Log("Hex: comparando " + main.Mem.Describe(View.Address) + " con A");
        }

        void DiffToLog()
        {
            if (View.Compare == null || !main.Attached)
            {
                main.Log("Hex: no hay comparación activa");
                return;
            }
            byte[] b = main.Mem.Read(View.CompareStart, View.Compare.Length);
            if (b == null) return;
            int count = 0;
            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < b.Length; k++)
            {
                if (b[k] == View.Compare[k]) continue;
                count++;
                if (count <= 200)
                    sb.AppendLine("    +0x" + k.ToString("X3") + "   A=" + View.Compare[k].ToString("X2") + "  B=" + b[k].ToString("X2") +
                                  "   bits A=" + Convert.ToString(View.Compare[k], 2).PadLeft(8, '0') + " B=" + Convert.ToString(b[k], 2).PadLeft(8, '0'));
            }
            main.Log("Hex: " + count + " bytes distintos entre A y " + main.Mem.Describe(View.CompareStart) + Environment.NewLine + sb.ToString().TrimEnd());
        }

        void WatchSelected()
        {
            if (View.Selected < 0) return;
            string d = main.Mem.Describe(SelectedAddress);
            main.AddWatchLine("Byte " + d + " | " + d + " | byte | cambia | {anterior} -> {valor} | log");
        }

        void UseAsPlayer()
        {
            main.SetSkillsBase(View.Selected >= 0 ? SelectedAddress : View.Address);
        }
    }

    class SkillsTab : UserControl
    {
        readonly MainForm main;
        readonly string path;
        readonly TextBox baseBox = new TextBox();
        readonly ListView list = new ListView();
        readonly Label info = new Label();
        List<Skill> skills = new List<Skill>();
        long baseAddr = -1, lastBase = -1;
        bool updating;

        public SkillsTab(MainForm main, string path)
        {
            this.main = main;
            this.path = path;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 34;
            bar.Padding = new Padding(2);
            Label l = new Label();
            l.Text = "Inicio del jugador:";
            l.AutoSize = true;
            l.Margin = new Padding(4, 8, 0, 0);
            bar.Controls.Add(l);
            baseBox.Width = 200;
            baseBox.Margin = new Padding(4, 5, 4, 0);
            baseBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ApplyText(); e.SuppressKeyPress = true; } };
            bar.Controls.Add(baseBox);
            bar.Controls.Add(MainForm.MakeButton("Aplicar", delegate { ApplyText(); }));
            bar.Controls.Add(MainForm.MakeButton("Ver en Hex", delegate { if (baseAddr > 0) main.ShowHex(baseAddr); }));
            bar.Controls.Add(MainForm.MakeButton("Editar habilidades.txt", delegate { Process.Start("notepad.exe", "\"" + path + "\""); }));
            bar.Controls.Add(MainForm.MakeButton("Recargar", delegate { LoadSkills(); }));

            info.Dock = DockStyle.Top;
            info.Height = 40;
            info.Padding = new Padding(6, 4, 6, 0);
            info.ForeColor = Color.DimGray;
            info.Text = "Marca o desmarca una habilidad para activarla o quitarla en memoria. " +
                        "Los cambios que haga el juego quedan en el log.";

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.Columns.Add("Habilidad", 220);
            list.Columns.Add("Offset", 70);
            list.Columns.Add("Bit", 40);
            list.Columns.Add("Estado", 70);
            list.Columns.Add("Byte", 160);
            list.ItemCheck += OnItemCheck;

            Controls.Add(list);
            Controls.Add(info);
            Controls.Add(bar);
        }

        public void LoadSkills()
        {
            skills = Skill.Load(path, main.Log);
            updating = true;
            list.Items.Clear();
            foreach (Skill s in skills)
            {
                ListViewItem it = new ListViewItem(new string[] { s.Name, "+0x" + s.Offset.ToString("X"), s.Bit.ToString(), "-", "-" });
                it.Tag = s;
                list.Items.Add(it);
            }
            updating = false;
            lastBase = -1;
            main.Log("habilidades.txt cargado: " + skills.Count + " habilidades");
        }

        public void SetBase(long addr)
        {
            baseAddr = addr;
            lastBase = -1;
            baseBox.Text = main.Mem.Describe(addr);
            main.Log("Habilidades: inicio de jugador = " + main.Mem.Describe(addr));
        }

        void ApplyText()
        {
            long a;
            if (main.Mem.Resolve(baseBox.Text, out a)) SetBase(a);
            else main.Log("Habilidades: dirección inválida o no legible: " + baseBox.Text);
        }

        public void Tick()
        {
            if (!main.Attached || baseAddr <= 0 || skills.Count == 0) return;
            int max = 0;
            foreach (Skill s in skills) max = Math.Max(max, s.Offset);
            byte[] b = main.Mem.Read(baseAddr, max + 1);
            if (b == null) return;

            updating = true;
            foreach (ListViewItem it in list.Items)
            {
                Skill s = (Skill)it.Tag;
                byte v = b[s.Offset];
                bool st = ((v >> s.Bit) & 1) == 1;
                if (s.HasState && s.State != st && lastBase == baseAddr)
                    main.Log("[Habilidad] " + s.Name + ": " + (s.State ? "SÍ" : "no") + " -> " + (st ? "SÍ" : "no") +
                             "  (jugador en " + main.Mem.Describe(baseAddr) + ")");
                s.State = st;
                s.HasState = true;
                if (it.Checked != st) it.Checked = st;
                string estado = st ? "SÍ" : "no";
                if (it.SubItems[3].Text != estado) it.SubItems[3].Text = estado;
                string bytes = "0x" + v.ToString("X2") + "  " + Convert.ToString(v, 2).PadLeft(8, '0');
                if (it.SubItems[4].Text != bytes) it.SubItems[4].Text = bytes;
            }
            updating = false;
            lastBase = baseAddr;
        }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (updating || baseAddr <= 0 || !main.Attached) return;
            Skill s = (Skill)list.Items[e.Index].Tag;
            long addr = baseAddr + s.Offset;
            byte[] b = main.Mem.Read(addr, 1);
            if (b == null) return;
            bool on = e.NewValue == CheckState.Checked;
            byte nv = on ? (byte)(b[0] | (1 << s.Bit)) : (byte)(b[0] & ~(1 << s.Bit));
            if (main.Mem.Write(addr, new byte[] { nv }))
                main.Log("Habilidades: " + s.Name + (on ? " activada" : " quitada") + " a mano (jugador en " + main.Mem.Describe(baseAddr) + ")");
            else
                main.Log("Habilidades: no se pudo escribir " + s.Name);
        }
    }

    class ScanRegion
    {
        public long Base;
        public byte[] Data;
        public BitArray Cand;
        public int Count;
    }

    // Escáner de memoria al estilo Cheat Engine: primer escaneo y escaneos sucesivos.
    class ScannerTab : UserControl
    {
        static readonly string[] TypeKeys = { "byte", "short", "int", "float", "texto", "texto16" };
        static readonly string[] TypeNames = { "Byte", "2 bytes", "4 bytes", "Float", "Texto", "Texto UTF-16" };
        static readonly string[] NextKeys = { "igual", "distinto", "cambio", "nocambio", "aumento", "disminuyo" };
        static readonly string[] NextNames = { "Igual a", "Distinto de", "Cambió", "No cambió", "Aumentó", "Disminuyó" };

        readonly MainForm main;
        readonly ComboBox typeBox = new ComboBox();
        readonly ComboBox firstMode = new ComboBox();
        readonly ComboBox nextMode = new ComboBox();
        readonly TextBox valueBox = new TextBox();
        readonly CheckBox ignoreCase = new CheckBox();
        readonly Label countLabel = new Label();
        readonly ListView results = new ListView();
        readonly Button nextBtn;

        List<ScanRegion> regions;
        string scanType;
        int scanLen, scanStep;
        volatile bool busy;

        public ScannerTab(MainForm main)
        {
            this.main = main;

            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.Dock = DockStyle.Top;
            row1.Height = 34;
            row1.Padding = new Padding(2);
            typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            typeBox.Items.AddRange(TypeNames);
            typeBox.SelectedIndex = 2;
            typeBox.Width = 100;
            typeBox.Margin = new Padding(4, 5, 4, 0);
            valueBox.Width = 180;
            valueBox.Margin = new Padding(4, 5, 4, 0);
            firstMode.DropDownStyle = ComboBoxStyle.DropDownList;
            firstMode.Items.AddRange(new object[] { "Valor exacto", "Valor desconocido" });
            firstMode.SelectedIndex = 0;
            firstMode.Width = 130;
            firstMode.Margin = new Padding(4, 5, 4, 0);
            ignoreCase.Text = "Ignorar mayúsculas";
            ignoreCase.Checked = true;
            ignoreCase.AutoSize = true;
            ignoreCase.Margin = new Padding(6, 8, 0, 0);
            row1.Controls.Add(MakeLabel("Tipo:"));
            row1.Controls.Add(typeBox);
            row1.Controls.Add(MakeLabel("Valor:"));
            row1.Controls.Add(valueBox);
            row1.Controls.Add(firstMode);
            row1.Controls.Add(MainForm.MakeButton("Primer escaneo", delegate { FirstScan(); }));
            row1.Controls.Add(ignoreCase);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Top;
            row2.Height = 34;
            row2.Padding = new Padding(2);
            nextMode.DropDownStyle = ComboBoxStyle.DropDownList;
            nextMode.Items.AddRange(NextNames);
            nextMode.SelectedIndex = 0;
            nextMode.Width = 120;
            nextMode.Margin = new Padding(4, 5, 4, 0);
            nextBtn = MainForm.MakeButton("Siguiente escaneo", delegate { NextScan(); });
            nextBtn.Enabled = false;
            countLabel.AutoSize = true;
            countLabel.Margin = new Padding(8, 8, 0, 0);
            countLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            row2.Controls.Add(MakeLabel("Siguiente:"));
            row2.Controls.Add(nextMode);
            row2.Controls.Add(nextBtn);
            row2.Controls.Add(MainForm.MakeButton("Nuevo", delegate { Reset(); }));
            row2.Controls.Add(MainForm.MakeButton("Ver en Hex", delegate { long a; if (SelectedAddr(out a)) main.ShowHex(a); }));
            row2.Controls.Add(MainForm.MakeButton("Agregar a watches", delegate { AddWatch(); }));
            row2.Controls.Add(MainForm.MakeButton("Copiar dirección", delegate { long a; if (SelectedAddr(out a)) Clipboard.SetText(main.Mem.Describe(a)); }));
            row2.Controls.Add(countLabel);

            results.Dock = DockStyle.Fill;
            results.View = View.Details;
            results.FullRowSelect = true;
            results.Font = new Font("Consolas", 9f);
            results.Columns.Add("Dirección", 160);
            results.Columns.Add("Valor actual", 200);
            results.Columns.Add("Valor en el escaneo", 200);
            results.DoubleClick += delegate { long a; if (SelectedAddr(out a)) main.ShowHex(a); };

            Controls.Add(results);
            Controls.Add(row2);
            Controls.Add(row1);
        }

        static Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(6, 8, 0, 0);
            return l;
        }

        static bool IsText(string type)
        {
            return type == "texto" || type == "texto16";
        }

        public void Reset()
        {
            if (busy) return;
            regions = null;
            results.Items.Clear();
            nextBtn.Enabled = false;
            typeBox.Enabled = true;
            countLabel.Text = "";
            GC.Collect();
        }

        bool ParseTarget(string type, out double num, out byte[] pattern)
        {
            num = 0;
            pattern = null;
            string s = valueBox.Text;
            if (type == "texto" || type == "texto16")
            {
                if (s.Length == 0) return false;
                pattern = type == "texto" ? Encoding.GetEncoding(1252).GetBytes(s) : Encoding.Unicode.GetBytes(s);
                return true;
            }
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long v;
                if (!GameMemory.ParseHex(s, out v)) return false;
                num = v;
            }
            else if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return false;

            // Ajustar al tamaño del tipo, así 255 y -1 coinciden en un byte, etc.
            if (type == "byte") num = (byte)(long)num;
            else if (type == "short") num = (short)(long)num;
            else if (type == "int") num = (int)(long)num;
            return true;
        }

        static byte Lower(byte b)
        {
            return ((b >= 65 && b <= 90) || (b >= 192 && b <= 222 && b != 215)) ? (byte)(b + 32) : b;
        }

        static bool PatternAt(byte[] d, int i, byte[] p, bool ic)
        {
            if (i + p.Length > d.Length) return false;
            for (int k = 0; k < p.Length; k++)
            {
                byte a = d[i + k], c = p[k];
                if (ic)
                {
                    a = Lower(a);
                    c = Lower(c);
                }
                if (a != c) return false;
            }
            return true;
        }

        static bool BytesEq(byte[] a, byte[] b, int i, int len)
        {
            for (int k = 0; k < len; k++)
                if (a[i + k] != b[i + k]) return false;
            return true;
        }

        static bool NumEq(string type, double v, double t)
        {
            return type == "float" ? Math.Abs(v - t) < 0.0005 + Math.Abs(t) * 1e-6 : v == t;
        }

        string FormatAt(byte[] d, int i)
        {
            if (i + scanLen > d.Length) return "?";
            if (scanType == "texto") return Clean(Encoding.GetEncoding(1252).GetString(d, i, scanLen));
            if (scanType == "texto16") return Clean(Encoding.Unicode.GetString(d, i, scanLen));
            double v = Values.Decode(scanType, d, i);
            string s = Values.Format(scanType, v);
            if (scanType != "float") s += "  (0x" + ((long)v & (scanLen == 1 ? 0xFF : scanLen == 2 ? 0xFFFF : 0xFFFFFFFFL)).ToString("X") + ")";
            return s;
        }

        static string Clean(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) sb.Append(c < 32 ? '.' : c);
            return sb.ToString();
        }

        void Progress(string text)
        {
            BeginInvoke(new Action(delegate { countLabel.Text = text; }));
        }

        void FirstScan()
        {
            if (busy) return;
            if (!main.Attached)
            {
                countLabel.Text = "El juego no está abierto";
                return;
            }
            string type = TypeKeys[typeBox.SelectedIndex];
            bool unknown = firstMode.SelectedIndex == 1;
            if (unknown && IsText(type))
            {
                countLabel.Text = "Valor desconocido no sirve para texto";
                return;
            }
            double target = 0;
            byte[] pattern = null;
            if (!unknown && !ParseTarget(type, out target, out pattern))
            {
                countLabel.Text = "Valor inválido";
                return;
            }
            bool ic = ignoreCase.Checked;
            int len = pattern != null ? pattern.Length : Values.SizeOf(type);
            int step = IsText(type) ? 1 : len;

            regions = null;
            results.Items.Clear();
            GC.Collect();
            scanType = type;
            scanLen = len;
            scanStep = step;
            busy = true;
            countLabel.Text = "Escaneando...";

            Thread t = new Thread(delegate()
            {
                List<ScanRegion> found = new List<ScanRegion>();
                List<MemRegion> mr = main.Mem.WritableRegions();
                DateTime lastProgress = DateTime.Now;
                for (int r = 0; r < mr.Count; r++)
                {
                    byte[] d = main.Mem.Read(mr[r].Base, mr[r].Size);
                    if (d == null) continue;
                    BitArray cand = new BitArray(d.Length);
                    int count = 0;
                    for (int i = 0; i + len <= d.Length; i += step)
                    {
                        bool ok;
                        if (unknown) ok = true;
                        else if (pattern != null) ok = PatternAt(d, i, pattern, ic);
                        else ok = NumEq(type, Values.Decode(type, d, i), target);
                        if (ok)
                        {
                            cand[i] = true;
                            count++;
                        }
                    }
                    if (count > 0) found.Add(new ScanRegion { Base = mr[r].Base, Data = d, Cand = cand, Count = count });
                    if ((DateTime.Now - lastProgress).TotalMilliseconds > 200)
                    {
                        lastProgress = DateTime.Now;
                        Progress("Escaneando... " + (r * 100 / mr.Count) + "%");
                    }
                }
                BeginInvoke(new Action(delegate { ScanDone(found, "Primer escaneo (" + TypeNames[Array.IndexOf(TypeKeys, type)] + ")"); }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void NextScan()
        {
            if (busy || regions == null) return;
            if (!main.Attached)
            {
                countLabel.Text = "El juego no está abierto";
                return;
            }
            string mode = NextKeys[nextMode.SelectedIndex];
            string type = scanType;
            bool text = IsText(type);
            if (text && (mode == "aumento" || mode == "disminuyo"))
            {
                countLabel.Text = "Aumentó/Disminuyó no aplica a texto";
                return;
            }
            double target = 0;
            byte[] pattern = null;
            if ((mode == "igual" || mode == "distinto") && !ParseTarget(type, out target, out pattern))
            {
                countLabel.Text = "Valor inválido";
                return;
            }
            bool ic = ignoreCase.Checked;
            int len = scanLen, step = scanStep;
            List<ScanRegion> current = regions;
            busy = true;
            countLabel.Text = "Escaneando...";

            Thread t = new Thread(delegate()
            {
                List<ScanRegion> kept = new List<ScanRegion>();
                foreach (ScanRegion rg in current)
                {
                    byte[] cur = main.Mem.Read(rg.Base, rg.Data.Length);
                    if (cur == null) continue;
                    int count = 0;
                    for (int i = 0; i + len <= cur.Length; i += step)
                    {
                        if (!rg.Cand[i]) continue;
                        bool keep;
                        if (mode == "cambio") keep = !BytesEq(rg.Data, cur, i, len);
                        else if (mode == "nocambio") keep = BytesEq(rg.Data, cur, i, len);
                        else if (text)
                        {
                            bool eq = PatternAt(cur, i, pattern, ic);
                            keep = mode == "igual" ? eq : !eq;
                        }
                        else
                        {
                            double n = Values.Decode(type, cur, i);
                            if (mode == "igual") keep = NumEq(type, n, target);
                            else if (mode == "distinto") keep = !NumEq(type, n, target);
                            else if (mode == "aumento") keep = n > Values.Decode(type, rg.Data, i);
                            else keep = n < Values.Decode(type, rg.Data, i);
                        }
                        if (keep) count++;
                        else rg.Cand[i] = false;
                    }
                    rg.Data = cur;
                    rg.Count = count;
                    if (count > 0) kept.Add(rg);
                }
                BeginInvoke(new Action(delegate { ScanDone(kept, "Siguiente escaneo (" + NextNames[Array.IndexOf(NextKeys, mode)] + ")"); }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void ScanDone(List<ScanRegion> found, string what)
        {
            busy = false;
            regions = found;
            long total = 0;
            foreach (ScanRegion rg in found) total += rg.Count;
            countLabel.Text = total.ToString("N0") + " resultados" + (total > 1000 ? " (se muestran 1.000)" : "");
            nextBtn.Enabled = true;
            typeBox.Enabled = false;
            main.Log("Escáner: " + what + " -> " + total.ToString("N0") + " resultados");

            results.BeginUpdate();
            results.Items.Clear();
            int shown = 0;
            foreach (ScanRegion rg in found)
            {
                for (int i = 0; i + scanLen <= rg.Data.Length && shown < 1000; i += scanStep)
                {
                    if (!rg.Cand[i]) continue;
                    long addr = rg.Base + i;
                    string v = FormatAt(rg.Data, i);
                    ListViewItem it = new ListViewItem(new string[] { main.Mem.Describe(addr), v, v });
                    it.Tag = addr;
                    results.Items.Add(it);
                    shown++;
                }
                if (shown >= 1000) break;
            }
            results.EndUpdate();
            GC.Collect();
        }

        public void Tick()
        {
            if (busy || !main.Attached || results.Items.Count == 0) return;
            int first = results.TopItem != null ? results.TopItem.Index : 0;
            int last = Math.Min(results.Items.Count, first + 60);
            for (int k = first; k < last; k++)
            {
                ListViewItem it = results.Items[k];
                byte[] b = main.Mem.Read((long)it.Tag, scanLen);
                string v = b != null ? FormatAt(b, 0) : "?";
                if (it.SubItems[1].Text != v) it.SubItems[1].Text = v;
            }
        }

        bool SelectedAddr(out long addr)
        {
            addr = 0;
            if (results.SelectedItems.Count == 0) return false;
            addr = (long)results.SelectedItems[0].Tag;
            return true;
        }

        void AddWatch()
        {
            long a;
            if (!SelectedAddr(out a)) return;
            if (IsText(scanType))
            {
                main.Log("Escáner: el texto no se puede vigilar en watches; usa \"Ver en Hex\"");
                return;
            }
            string d = main.Mem.Describe(a);
            main.AddWatchLine("Valor " + d + " | " + d + " | " + scanType + " | cambia | {anterior} -> {valor} | log");
        }
    }

    // Una jugada oculta: bits que la función 0x4DBCF0 del juego decide según el ID del jugador.
    class HiddenMove
    {
        public readonly string Key, OffKey;
        public string Label;
        public readonly int Field;
        public readonly uint Mask, Value;

        public HiddenMove(string key, string label, int field, uint mask, uint value, string offKey)
        {
            Key = key;
            Label = label;
            Field = field;
            Mask = mask;
            Value = value;
            OffKey = offKey;
        }

        public bool In(uint c20, uint c24)
        {
            return ((Field == 0x20 ? c20 : c24) & Mask) == Value;
        }
    }

    class ExtraLine
    {
        public int Id;
        public string Key, Comment;
    }

    // Pestaña "Jugadas ocultas": prende y apaga jugadas por jugador.
    // aplicar_jugadas.ps1 engancha la función 0x4DBCF0 con una tabla propia en 0xACD400;
    // con el juego abierto esta pestaña reescribe esa tabla en memoria (vale desde el próximo partido),
    // y con el juego cerrado actualiza WE9.exe ejecutando el script.
    class HiddenMovesTab : UserControl
    {
        const long Func = 0x4DBCF0, Cave = 0xACD390, Table = 0xACD400, CaveEnd = 0xACDF80;
        const long PlayerTablePtr = 0x38B77C4; // WE9.exe+34B77C4 -> ficha del jugador con ID 0
        const int RecordSize = 0x7C, MaxPlayers = 8192;

        readonly MainForm main;
        readonly string dir, extraPath, originalPath, namesPath, labelsPath, adnPath, teamsPath;
        readonly List<HiddenMove> moves = new List<HiddenMove>();
        readonly Dictionary<int, uint[]> original = new Dictionary<int, uint[]>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();
        readonly Dictionary<int, string> adn = new Dictionary<int, string>();
        readonly Dictionary<int, string> teams = new Dictionary<int, string>();
        Dictionary<int, string> firstRead;
        readonly List<string> headerLines = new List<string>();
        List<ExtraLine> extras = new List<ExtraLine>();

        readonly TextBox search = new TextBox();
        readonly ListView list = new ListView();
        readonly Label playerLabel = new Label();
        readonly Label status = new Label();
        readonly FlowLayoutPanel checks = new FlowLayoutPanel();
        readonly CheckBox onlyChanged = new CheckBox();
        readonly List<CheckBox> boxes = new List<CheckBox>();
        int selectedId = -1;
        bool updating;
        public bool PendingFile;
        DateTime lastNameTry = DateTime.MinValue;

        public HiddenMovesTab(MainForm main, string dir)
        {
            this.main = main;
            this.dir = dir;
            extraPath = Path.Combine(dir, "jugadas_extra.txt");
            originalPath = Path.Combine(dir, "jugadas_originales.csv");
            namesPath = Path.Combine(dir, "nombres_jugadores.txt");
            labelsPath = Path.Combine(dir, "jugadas_nombres.txt");
            adnPath = Path.Combine(dir, "adn_jugadores.txt");
            teamsPath = Path.Combine(dir, "equipos_jugadores.txt");

            moves.Add(new HiddenMove("elastica", "Elástica", 0x20, 0x600000, 0x200000, "sin_elastica"));
            moves.Add(new HiddenMove("elastica2", "Elástica 2", 0x20, 0x600000, 0x400000, "sin_elastica"));
            foreach (uint bit in new uint[] { 0x20000, 0x40000, 0x80000, 0x100000 })
            {
                string n = bit.ToString("x6");
                moves.Add(new HiddenMove("oculta_" + n, "Oculta " + n, 0x20, bit, bit, "sin_oculta_" + n));
            }
            foreach (uint bit in new uint[] { 0x1, 0x40000000, 0x80000000 })
            {
                string n = bit.ToString("X8");
                moves.Add(new HiddenMove("24+" + n, "Oculta 24:" + bit.ToString("X"), 0x24, bit, bit, "24-" + n));
            }
            LoadLabels();

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 34;
            bar.Padding = new Padding(2);
            Label l = new Label();
            l.Text = "Buscar (nombre, ADN, equipo o ID):";
            l.AutoSize = true;
            l.Margin = new Padding(4, 8, 0, 0);
            bar.Controls.Add(l);
            search.Width = 200;
            search.Margin = new Padding(4, 5, 4, 0);
            search.TextChanged += delegate { Fill(); };
            bar.Controls.Add(search);
            bar.Controls.Add(MainForm.MakeButton("Editar nombres de jugadas", delegate { EditLabels(); }));
            onlyChanged.Text = "Mostrar solo los que cambié";
            onlyChanged.AutoSize = true;
            onlyChanged.Margin = new Padding(8, 8, 0, 0);
            onlyChanged.CheckedChanged += delegate { Fill(); };
            bar.Controls.Add(onlyChanged);
            bar.Controls.Add(MainForm.MakeButton("Restaurar todo por defecto", delegate { RestoreAll(); }));
            status.AutoSize = true;
            status.Margin = new Padding(8, 8, 0, 0);
            status.ForeColor = Color.DimGray;
            bar.Controls.Add(status);

            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 260;
            side.Padding = new Padding(8);
            playerLabel.Dock = DockStyle.Top;
            playerLabel.Height = 64;
            playerLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            playerLabel.Text = "Elige un jugador";
            checks.Dock = DockStyle.Top;
            checks.Height = 260;
            checks.FlowDirection = FlowDirection.TopDown;
            foreach (HiddenMove m in moves)
            {
                CheckBox cb = new CheckBox();
                cb.AutoSize = true;
                cb.Tag = m;
                cb.Enabled = false;
                cb.CheckedChanged += OnCheck;
                boxes.Add(cb);
                checks.Controls.Add(cb);
            }
            Button restore = MainForm.MakeButton("Dejar como el original", delegate { RestoreOriginal(); });
            restore.Dock = DockStyle.Top;
            Label help = new Label();
            help.Dock = DockStyle.Fill;
            help.ForeColor = Color.DimGray;
            help.Padding = new Padding(0, 8, 0, 0);
            help.Text = "Con el juego abierto, los cambios valen desde el próximo partido " +
                        "(o al volver a entrar a práctica).\n\n✓ = la tiene\n✓ + = agregada por ti\n– = se la quitaste";
            side.Controls.Add(help);
            side.Controls.Add(restore);
            side.Controls.Add(checks);
            side.Controls.Add(playerLabel);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.Columns.Add("ID", 55);
            list.Columns.Add("Jugador", 140);
            list.Columns.Add("ADN", 110);
            list.Columns.Add("Equipo", 120);
            foreach (HiddenMove m in moves) list.Columns.Add(m.Label, 78);
            list.SelectedIndexChanged += delegate
            {
                if (list.SelectedItems.Count > 0) ShowPlayer((int)list.SelectedItems[0].Tag);
            };

            Controls.Add(list);
            Controls.Add(side);
            Controls.Add(bar);
        }

        public void LoadAll()
        {
            original.Clear();
            if (File.Exists(originalPath))
                foreach (string line in File.ReadAllLines(originalPath).Skip(1))
                {
                    string[] c = line.Split(',');
                    if (c.Length < 3) continue;
                    original[int.Parse(c[0])] = new uint[] { Convert.ToUInt32(c[1], 16), Convert.ToUInt32(c[2], 16) };
                }
            ReadIdFile(namesPath, names);
            ReadIdFile(adnPath, adn);
            ReadIdFile(teamsPath, teams);
            LoadExtras();
            Fill();
        }

        static void ReadIdFile(string path, Dictionary<int, string> into)
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int p = line.IndexOf('|');
                int id;
                if (p > 0 && int.TryParse(line.Substring(0, p), out id)) into[id] = line.Substring(p + 1);
            }
        }

        void LoadLabels()
        {
            if (!File.Exists(labelsPath))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Nombre que se muestra para cada jugada:  clave | nombre");
                sb.AppendLine("# Cuando descubras qué es una jugada oculta, cámbiale el nombre aquí.");
                foreach (HiddenMove m in moves) sb.AppendLine(m.Key + " | " + m.Label);
                File.WriteAllText(labelsPath, sb.ToString(), Encoding.UTF8);
                return;
            }
            foreach (string line in File.ReadAllLines(labelsPath, Encoding.UTF8))
            {
                if (line.TrimStart().StartsWith("#")) continue;
                string[] c = line.Split('|');
                if (c.Length < 2) continue;
                foreach (HiddenMove m in moves)
                    if (m.Key == c[0].Trim().ToLowerInvariant() && c[1].Trim().Length > 0) m.Label = c[1].Trim();
            }
        }

        void EditLabels()
        {
            Process p = Process.Start("notepad.exe", "\"" + labelsPath + "\"");
            p.EnableRaisingEvents = true;
            p.Exited += delegate { BeginInvoke(new Action(delegate { LoadLabels(); RefreshLabels(); })); };
        }

        void RefreshLabels()
        {
            for (int i = 0; i < moves.Count; i++)
            {
                list.Columns[i + 4].Text = moves[i].Label;
                boxes[i].Text = moves[i].Label;
            }
        }

        // jugadas_extra.txt: comentarios arriba, después líneas "ID | jugada  # nombre".
        void LoadExtras()
        {
            headerLines.Clear();
            extras = new List<ExtraLine>();
            if (!File.Exists(extraPath)) return;
            foreach (string raw in File.ReadAllLines(extraPath, Encoding.UTF8))
            {
                int hash = raw.IndexOf('#');
                string body = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (body.Length == 0)
                {
                    if (extras.Count == 0) headerLines.Add(raw);
                    continue;
                }
                string[] c = body.Split('|');
                int id;
                if (c.Length != 2 || !int.TryParse(c[0].Trim(), out id))
                {
                    main.Log("jugadas_extra.txt: línea inválida: " + raw);
                    continue;
                }
                ExtraLine e = new ExtraLine();
                e.Id = id;
                e.Key = c[1].Trim().ToLowerInvariant();
                e.Comment = hash >= 0 ? raw.Substring(hash + 1).Trim() : "";
                extras.Add(e);
            }
            while (headerLines.Count > 0 && headerLines[headerLines.Count - 1].Trim().Length == 0)
                headerLines.RemoveAt(headerLines.Count - 1);
        }

        void SaveExtras()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string h in headerLines) sb.AppendLine(h);
            sb.AppendLine();
            foreach (ExtraLine e in extras)
            {
                string name = NameOf(e.Id);
                string comment = name.Length > 0 ? name : e.Comment;
                sb.AppendLine((e.Id + " | " + e.Key).PadRight(26) + (comment.Length > 0 ? "# " + comment : ""));
            }
            File.WriteAllText(extraPath, sb.ToString(), Encoding.UTF8);
        }

        // and20, or20, and24, or24 de una línea (mismo formato que aplicar_jugadas.ps1)
        static uint[] Encode(string key)
        {
            if (key == "elastica") return new uint[] { 0xFF9FFFFF, 0x200000, 0xFFFFFFFF, 0 };
            if (key == "elastica2") return new uint[] { 0xFF9FFFFF, 0x400000, 0xFFFFFFFF, 0 };
            if (key == "sin_elastica") return new uint[] { 0xFF9FFFFF, 0, 0xFFFFFFFF, 0 };
            bool off = key.StartsWith("sin_oculta_");
            if (off || key.StartsWith("oculta_"))
            {
                uint bit = Convert.ToUInt32(key.Substring(key.LastIndexOf('_') + 1), 16);
                return off ? new uint[] { ~bit, 0, 0xFFFFFFFF, 0 } : new uint[] { 0xFFFFFFFF, bit, 0xFFFFFFFF, 0 };
            }
            if (key.Length >= 4 && (key.StartsWith("20") || key.StartsWith("24")) && (key[2] == '+' || key[2] == '-'))
            {
                uint bits = Convert.ToUInt32(key.Substring(3), 16);
                uint and = key[2] == '+' ? 0xFFFFFFFF : ~bits, or = key[2] == '+' ? bits : 0;
                return key.StartsWith("20") ? new uint[] { and, or, 0xFFFFFFFF, 0 } : new uint[] { 0xFFFFFFFF, 0, and, or };
            }
            return null;
        }

        uint[] Effective(int id)
        {
            uint[] o;
            uint c20 = 0, c24 = 0;
            if (original.TryGetValue(id, out o)) { c20 = o[0]; c24 = o[1]; }
            foreach (ExtraLine e in extras)
            {
                if (e.Id != id) continue;
                uint[] enc = Encode(e.Key);
                if (enc == null) continue;
                c20 = (c20 & enc[0]) | enc[1];
                c24 = (c24 & enc[2]) | enc[3];
            }
            return new uint[] { c20, c24 };
        }

        uint[] Original(int id)
        {
            uint[] o;
            return original.TryGetValue(id, out o) ? o : new uint[] { 0, 0 };
        }

        string NameOf(int id)
        {
            string n;
            return names.TryGetValue(id, out n) ? n : "";
        }

        // Nombres desde la memoria del juego (tabla de jugadores apuntada por WE9.exe+34B77C4).
        // Al abrir el juego la tabla tiene los nombres de fábrica (el "ADN"); al cargar las
        // ediciones cambian. Se relee cada 10 s; si cambia mucho, la primera lectura era el ADN.
        public void Tick()
        {
            if (!main.Attached || (DateTime.Now - lastNameTry).TotalSeconds < 10) return;
            lastNameTry = DateTime.Now;
            bool teamsChanged = UpdateTeams();
            Dictionary<int, string> read = ReadNames();
            int changed = read == null ? 0 : read.Count(kv => NameOf(kv.Key) != kv.Value);
            if (changed == 0)
            {
                if (teamsChanged) Fill();
                return;
            }

            if (firstRead == null) firstRead = read;
            else if (adn.Count == 0 && read.Count(kv => { string f; return firstRead.TryGetValue(kv.Key, out f) && f != kv.Value; }) > 100)
            {
                foreach (KeyValuePair<int, string> kv in firstRead) adn[kv.Key] = kv.Value;
                File.WriteAllLines(adnPath, adn.Select(kv => kv.Key + "|" + kv.Value), Encoding.UTF8);
                main.Log("Jugadas ocultas: guardados los nombres de fábrica (ADN) de " + adn.Count + " jugadores");
            }
            names.Clear();
            foreach (KeyValuePair<int, string> kv in read) names[kv.Key] = kv.Value;
            File.WriteAllLines(namesPath, names.Select(kv => kv.Key + "|" + kv.Value), Encoding.UTF8);
            main.Log("Jugadas ocultas: " + changed + " nombres de jugadores actualizados desde el juego");
            Fill();
        }

        Dictionary<int, string> ReadNames()
        {
            byte[] p = main.Mem.Read(PlayerTablePtr, 4);
            if (p == null) return null;
            long baseAddr = BitConverter.ToUInt32(p, 0);
            if (baseAddr == 0) return null;
            byte[] t = null;
            int count = MaxPlayers;
            while (count >= 1000 && (t = main.Mem.Read(baseAddr, count * RecordSize)) == null) count -= 500;
            if (t == null) return null;
            Dictionary<int, string> read = new Dictionary<int, string>();
            for (int id = 0; id < count; id++)
            {
                string n = Encoding.Unicode.GetString(t, id * RecordSize, 28).Split('\0')[0].Trim();
                if (n.Length > 0 && !n.Any(ch => ch < 0x20)) read[id] = n;
            }
            return read.Count < 100 ? null : read; // la tabla todavía no está cargada
        }

        // Plantillas de clubes: 138 clubes x 32 lugares (IDs de 2 bytes) y nombres de 140 bytes por club.
        // Direcciones fijas en la zona de datos de WE9.exe.
        const long ClubRosters = 0x038BB368, ClubNames = 0x038CEEC8;
        const int Clubs = 138, ClubSlots = 32, ClubNameSize = 0x8C;

        bool UpdateTeams()
        {
            byte[] r = main.Mem.Read(ClubRosters, Clubs * ClubSlots * 2);
            byte[] n = main.Mem.Read(ClubNames, Clubs * ClubNameSize);
            if (r == null || n == null) return false;
            Dictionary<int, string> read = new Dictionary<int, string>();
            Encoding latin = Encoding.GetEncoding(1252);
            for (int c = 0; c < Clubs; c++)
            {
                string name = latin.GetString(n, c * ClubNameSize, 48).Split('\0')[0].Trim();
                for (int s = 0; s < ClubSlots; s++)
                {
                    int id = BitConverter.ToUInt16(r, (c * ClubSlots + s) * 2);
                    if (id > 0 && !read.ContainsKey(id)) read[id] = name;
                }
            }
            if (read.Count < 500) return false; // todavía no cargado
            if (read.Count == teams.Count && read.All(kv => TeamOf(kv.Key) == kv.Value)) return false;
            teams.Clear();
            foreach (KeyValuePair<int, string> kv in read) teams[kv.Key] = kv.Value;
            File.WriteAllLines(teamsPath, teams.Select(kv => kv.Key + "|" + kv.Value), Encoding.UTF8);
            main.Log("Jugadas ocultas: equipos actualizados desde el juego (" + teams.Count + " jugadores en clubes)");
            return true;
        }

        string TeamOf(int id)
        {
            string t;
            return teams.TryGetValue(id, out t) ? t : "";
        }

        string AdnOf(int id)
        {
            string n;
            return adn.TryGetValue(id, out n) && n != NameOf(id) ? n : "";
        }

        void Fill()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            IEnumerable<int> ids;
            if (onlyChanged.Checked)
                ids = extras.Select(e => e.Id).Distinct().OrderBy(i => i);
            else if (q.Length == 0)
            {
                // Sin búsqueda: solo los que tienen alguna jugada (original o agregada)
                HashSet<int> set = new HashSet<int>(extras.Select(e => e.Id));
                foreach (KeyValuePair<int, uint[]> kv in original)
                    if (kv.Value[0] != 0 || kv.Value[1] != 0) set.Add(kv.Key);
                ids = set.OrderBy(i => i);
            }
            else
            {
                int qid;
                bool isNum = int.TryParse(q, out qid);
                ids = Enumerable.Range(0, MaxPlayers).Where(i => (isNum && i.ToString().StartsWith(q)) || NameOf(i).ToLowerInvariant().Contains(q) || AdnOf(i).ToLowerInvariant().Contains(q) || TeamOf(i).ToLowerInvariant().Contains(q));
            }

            list.BeginUpdate();
            list.Items.Clear();
            foreach (int id in ids.Take(500))
            {
                ListViewItem it = new ListViewItem(new string[] { id.ToString(), NameOf(id), AdnOf(id), TeamOf(id) });
                it.Tag = id;
                uint[] eff = Effective(id), orig = Original(id);
                foreach (HiddenMove m in moves)
                {
                    bool now = m.In(eff[0], eff[1]), was = m.In(orig[0], orig[1]);
                    it.SubItems.Add(now ? (was ? "✓" : "✓ +") : (was ? "–" : ""));
                }
                if (extras.Any(e => e.Id == id)) it.ForeColor = Color.DarkBlue;
                list.Items.Add(it);
                if (id == selectedId) it.Selected = true;
            }
            list.EndUpdate();
            status.Text = extras.Count + " cambios propios";
        }

        void ShowPlayer(int id)
        {
            selectedId = id;
            playerLabel.Text = id + "  " + NameOf(id) + (AdnOf(id).Length > 0 ? "\nADN: " + AdnOf(id) : "") + "\nEquipo: " + (TeamOf(id).Length > 0 ? TeamOf(id) : "sin club");
            uint[] eff = Effective(id);
            updating = true;
            for (int i = 0; i < moves.Count; i++)
            {
                boxes[i].Text = moves[i].Label;
                boxes[i].Checked = moves[i].In(eff[0], eff[1]);
                boxes[i].Enabled = true;
            }
            updating = false;
        }

        void OnCheck(object sender, EventArgs e)
        {
            if (updating || selectedId < 0) return;
            CheckBox cb = (CheckBox)sender;
            HiddenMove m = (HiddenMove)cb.Tag;
            int id = selectedId;
            bool on = cb.Checked;

            // Quitar lo que ya hubiera para esta jugada (las dos elásticas comparten campo)
            extras.RemoveAll(x => x.Id == id && Related(x.Key, m));
            uint[] eff = Effective(id);
            if (m.In(eff[0], eff[1]) != on)
            {
                ExtraLine x = new ExtraLine();
                x.Id = id;
                x.Key = on ? m.Key : m.OffKey;
                x.Comment = "";
                extras.Add(x);
            }
            main.Log("Jugadas ocultas: " + id + " " + NameOf(id) + " -> " + m.Label + (on ? " ON" : " OFF"));
            Apply();
            ShowPlayer(id);
        }

        static bool Related(string key, HiddenMove m)
        {
            if (m.Mask == 0x600000) return key == "elastica" || key == "elastica2" || key == "sin_elastica";
            return key == m.Key || key == m.OffKey;
        }

        void RestoreAll()
        {
            if (extras.Count == 0)
            {
                main.Log("Jugadas ocultas: no hay cambios propios, todo está como de fábrica");
                return;
            }
            string who = string.Join(", ", extras.Select(x => x.Id).Distinct().Take(10).Select(id => NameOf(id).Length > 0 ? NameOf(id) : id.ToString()));
            int players = extras.Select(x => x.Id).Distinct().Count();
            if (MessageBox.Show("Se van a quitar todos los cambios propios (" + players + " jugadores: " + who +
                                (players > 10 ? "…" : "") + ") y cada jugador vuelve a sus jugadas de fábrica.\n\n¿Seguro?",
                                "Restaurar todo por defecto", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            extras.Clear();
            main.Log("Jugadas ocultas: restaurado todo por defecto (" + players + " jugadores)");
            Apply();
            if (selectedId >= 0) ShowPlayer(selectedId);
        }

        void RestoreOriginal()
        {
            if (selectedId < 0) return;
            extras.RemoveAll(x => x.Id == selectedId);
            main.Log("Jugadas ocultas: " + selectedId + " " + NameOf(selectedId) + " vuelve a sus jugadas originales");
            Apply();
            ShowPlayer(selectedId);
        }

        void Apply()
        {
            SaveExtras();
            Fill();
            if (main.Attached)
            {
                string err = WriteToGame();
                if (err == null) main.Log("Jugadas ocultas: tabla actualizada en el juego (vale desde el próximo partido)");
                else main.Log("Jugadas ocultas: " + err);
                PendingFile = true; // WE9.exe está bloqueado; se actualiza al cerrar el juego
            }
            else ApplyToFile();
        }

        byte[] BuildTable()
        {
            int max = (int)((CaveEnd - Table) / 20) - 1;
            List<ExtraLine> valid = extras.Where(x => Encode(x.Key) != null).ToList();
            if (valid.Count > max) return null;
            byte[] t = new byte[(valid.Count + 1) * 20];
            int pos = 0;
            foreach (ExtraLine x in valid)
            {
                BitConverter.GetBytes((ushort)x.Id).CopyTo(t, pos);
                uint[] enc = Encode(x.Key);
                for (int k = 0; k < 4; k++) BitConverter.GetBytes(enc[k]).CopyTo(t, pos + 4 + 4 * k);
                pos += 20;
            }
            t[pos] = 0xFF;
            t[pos + 1] = 0xFF;
            return t;
        }

        string WriteToGame()
        {
            byte[] hook = main.Mem.Read(Func, 5);
            if (hook == null || hook[0] != 0xE9 || Func + 5 + BitConverter.ToInt32(hook, 1) != Cave)
                return "el juego abierto no tiene el parche; ciérralo y se aplicará a WE9.exe";
            byte[] t = BuildTable();
            if (t == null) return "demasiados cambios para la tabla";
            if (!main.Mem.WriteProtected(Table, t))
                return "no se pudo escribir en el juego (¿abrir el monitor como administrador?)";
            return null;
        }

        public void ApplyToFile()
        {
            if (!File.Exists(extraPath)) return;
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(dir, "aplicar_jugadas.ps1") + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    foreach (string line in output.Split('\n'))
                        if (line.Trim().Length > 0 && !line.StartsWith("At ") && !line.TrimStart().StartsWith("+"))
                            main.Log("aplicar_jugadas: " + line.Trim());
                    PendingFile = p.ExitCode != 0;
                }
            }
            catch (Exception ex)
            {
                main.Log("aplicar_jugadas: " + ex.Message);
            }
        }
    }

    // Pestaña "Tiro al ángulo": por ahora es de investigación. Muestra en vivo las variables del
    // cálculo del tiro (configurables en tiro_angulo.txt) y anota en el log cada cambio, para
    // encontrar dónde decide el juego el destino del tiro libre. El forzado viene después.
    class FreeKickTab : UserControl
    {
        class Var
        {
            public string Name, Expr, Type, Display = "-";
            public bool Has;
            public double Last;
        }

        readonly MainForm main;
        readonly string path;
        readonly ListView list = new ListView();
        readonly CheckBox force = new CheckBox();
        List<Var> vars = new List<Var>();
        bool updating;

        // En 0x65D774 el juego suma al ángulo del tiro un desvío al azar según la precisión
        // del pateador ("add edi, eax"). Cambiarlo por dos NOP deja el tiro exacto.
        const long ErrorAdd = 0x65D774;
        static readonly byte[] ErrorAddOriginal = { 0x03, 0xF8 };
        static readonly byte[] ErrorAddOff = { 0x90, 0x90 };

        void SetNoError(bool on)
        {
            byte[] cur = main.Mem.Read(ErrorAdd, 2);
            if (cur == null || !(Same(cur, ErrorAddOriginal) || Same(cur, ErrorAddOff)))
            {
                main.Log("Tiro al ángulo: el código del juego no es el esperado, no se modifica nada");
                return;
            }
            if (main.Mem.WriteProtected(ErrorAdd, on ? ErrorAddOff : ErrorAddOriginal))
                main.Log("Tiro al ángulo: desvío del tiro " + (on ? "DESACTIVADO (tiro exacto)" : "normal"));
            else
                main.Log("Tiro al ángulo: no se pudo escribir en el juego (¿abrir el monitor como administrador?)");
        }

        static bool Same(byte[] a, byte[] b)
        {
            return a.Length == b.Length && a.Zip(b, (x, y) => x == y).All(x => x);
        }

        public FreeKickTab(MainForm main, string dir, Action markFreeKick)
        {
            this.main = main;
            path = Path.Combine(dir, "tiro_angulo.txt");

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 36;
            bar.Padding = new Padding(4);
            bar.Controls.Add(MainForm.MakeButton("Registrar tiro libre (F8)", delegate { markFreeKick(); }));
            bar.Controls.Add(MainForm.MakeButton("Editar variables", delegate
            {
                Process p = Process.Start("notepad.exe", "\"" + path + "\"");
                p.EnableRaisingEvents = true;
                p.Exited += delegate { BeginInvoke(new Action(LoadVars)); };
            }));
            force.Text = "Tiro libre sin desvío (va exacto a donde apuntás)";
            force.AutoSize = true;
            force.Enabled = false;
            force.Margin = new Padding(12, 8, 0, 0);
            force.CheckedChanged += delegate { if (!updating) SetNoError(force.Checked); };
            bar.Controls.Add(force);

            Label help = new Label();
            help.Dock = DockStyle.Top;
            help.Height = 58;
            help.Padding = new Padding(8, 4, 8, 0);
            help.ForeColor = Color.DimGray;
            help.Text = "Cómo ayudar a encontrar el destino del tiro: patea tiros libres apuntando a lugares distintos " +
                        "(ángulo izquierdo, derecho, al medio, arriba, abajo) y aprieta F8 justo después de cada uno. " +
                        "Anota a dónde fue cada tiro (\"tiro 1: ángulo derecho arriba\"). Los valores de abajo se anotan solos en el log.";

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Font = new Font("Consolas", 9f);
            list.Columns.Add("Variable", 260);
            list.Columns.Add("Dirección", 140);
            list.Columns.Add("Tipo", 60);
            list.Columns.Add("Valor", 160);

            Controls.Add(list);
            Controls.Add(help);
            Controls.Add(bar);
        }

        public void LoadVars()
        {
            if (!File.Exists(path))
                File.WriteAllText(path,
                    "# Variables del tiro que se muestran en la pestaña \"Tiro al ángulo\"\r\n" +
                    "# nombre | dirección | tipo (byte, short, int, float)\r\n" +
                    "# Encontradas en el código del tiro (WE9.exe 0x65A3xx):\r\n" +
                    "Potencia / efecto del tiro | 0x3BE1420 | short\r\n" +
                    "Tiro especial activo | 0x3B8E883 | byte\r\n" +
                    "Estado de pelota parada | 0x3B8E88D | byte\r\n", Encoding.UTF8);
            vars = new List<Var>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] c = line.Split('|');
                if (c.Length < 3) continue;
                Var v = new Var();
                v.Name = c[0].Trim();
                v.Expr = c[1].Trim();
                v.Type = c[2].Trim().ToLowerInvariant();
                vars.Add(v);
            }
            list.Items.Clear();
            foreach (Var v in vars)
            {
                ListViewItem it = new ListViewItem(new string[] { v.Name, v.Expr, v.Type, "-" });
                it.Tag = v;
                list.Items.Add(it);
            }
        }

        public void Tick()
        {
            if (!main.Attached)
            {
                force.Enabled = false;
                return;
            }
            // La casilla refleja lo que hay en la memoria del juego (al reabrir el juego vuelve a normal)
            byte[] cur = main.Mem.Read(ErrorAdd, 2);
            bool known = cur != null && (Same(cur, ErrorAddOriginal) || Same(cur, ErrorAddOff));
            force.Enabled = known;
            updating = true;
            force.Checked = known && Same(cur, ErrorAddOff);
            updating = false;

            foreach (ListViewItem it in list.Items)
            {
                Var v = (Var)it.Tag;
                long addr;
                byte[] b = null;
                if (main.Mem.Resolve(v.Expr, out addr)) b = main.Mem.Read(addr, Values.SizeOf(v.Type));
                if (b == null)
                {
                    v.Has = false;
                    v.Display = "sin leer";
                }
                else
                {
                    double val = Values.Decode(v.Type, b, 0);
                    if (v.Has && val != v.Last)
                        main.Log("[Tiro] " + v.Name + ": " + Values.Format(v.Type, v.Last) + " -> " + Values.Format(v.Type, val));
                    v.Has = true;
                    v.Last = val;
                    long raw = v.Type == "float" ? 0 : (long)val & (b.Length == 1 ? 0xFF : b.Length == 2 ? 0xFFFF : 0xFFFFFFFFL);
                    v.Display = Values.Format(v.Type, val) + (v.Type == "float" ? "" : "  (0x" + raw.ToString("X") + ")");
                }
                if (it.SubItems[3].Text != v.Display) it.SubItems[3].Text = v.Display;
            }
        }
    }

    // Pestaña "Movimientos": qué animación hace cada jugador en el partido.
    // Jugadores en partido: 23 estructuras de 0x290 bytes desde 0x3BCF880 (+0x3F número, +0x40 equipo,
    // +0x120/+0x128 posición, +4 puntero a su animación; en la animación, +0x30 = número de movimiento).
    class AnimTab : UserControl
    {
        const long PlayersBase = 0x3BCF880;
        const int PlayerSize = 0x290, PlayerCount = 23;

        readonly MainForm main;
        readonly string seenPath, namesPath;
        readonly ListView players = new ListView();
        readonly ListView seen = new ListView();
        readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        readonly Dictionary<int, string> firstSeen = new Dictionary<int, string>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();
        readonly int[] last = new int[PlayerCount];
        DateTime lastUi = DateTime.MinValue, lastSave = DateTime.MinValue;
        bool dirty;

        public AnimTab(MainForm main, string dir)
        {
            this.main = main;
            seenPath = Path.Combine(dir, "movimientos_vistos.txt");
            namesPath = Path.Combine(dir, "movimientos_nombres.txt");
            for (int i = 0; i < PlayerCount; i++) last[i] = -1;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 36;
            bar.Padding = new Padding(4);
            bar.Controls.Add(MainForm.MakeButton("Editar nombres de movimientos", delegate
            {
                if (!File.Exists(namesPath))
                    File.WriteAllText(namesPath, "# Nombre para cada número de movimiento:  0x1BF | parado\r\n", Encoding.UTF8);
                Process p = Process.Start("notepad.exe", "\"" + namesPath + "\"");
                p.EnableRaisingEvents = true;
                p.Exited += delegate { BeginInvoke(new Action(delegate { LoadNames(); RefreshSeen(); })); };
            }));
            bar.Controls.Add(MainForm.MakeButton("Borrar lista de vistos", delegate
            {
                counts.Clear();
                firstSeen.Clear();
                dirty = true;
                RefreshSeen();
            }));
            Label help = new Label();
            help.AutoSize = true;
            help.Margin = new Padding(8, 8, 0, 0);
            help.ForeColor = Color.DimGray;
            help.Text = "Cada movimiento nuevo queda en el log. Los raros (pocas veces) son los especiales.";
            bar.Controls.Add(help);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 430;

            players.Dock = DockStyle.Fill;
            players.View = View.Details;
            players.FullRowSelect = true;
            players.Font = new Font("Consolas", 9f);
            players.Columns.Add("#", 30);
            players.Columns.Add("Equipo", 55);
            players.Columns.Add("Núm", 45);
            players.Columns.Add("Movimiento", 200);
            players.Columns.Add("Posición", 110);
            for (int i = 0; i < PlayerCount; i++)
                players.Items.Add(new ListViewItem(new string[] { i.ToString(), "-", "-", "-", "-" }));

            seen.Dock = DockStyle.Fill;
            seen.View = View.Details;
            seen.FullRowSelect = true;
            seen.Font = new Font("Consolas", 9f);
            seen.Columns.Add("Movimiento", 90);
            seen.Columns.Add("Nombre", 160);
            seen.Columns.Add("Veces", 60);
            seen.Columns.Add("Primera vez", 90);

            split.Panel1.Controls.Add(players);
            split.Panel2.Controls.Add(seen);
            Controls.Add(split);
            Controls.Add(bar);
        }

        public void LoadAll()
        {
            LoadNames();
            if (File.Exists(seenPath))
                foreach (string line in File.ReadAllLines(seenPath))
                {
                    string[] c = line.Split('|');
                    long id;
                    int n;
                    if (c.Length >= 3 && GameMemory.ParseHex(c[0], out id) && int.TryParse(c[1], out n))
                    {
                        counts[(int)id] = n;
                        firstSeen[(int)id] = c[2];
                    }
                }
            RefreshSeen();
        }

        void LoadNames()
        {
            names.Clear();
            if (!File.Exists(namesPath)) return;
            foreach (string line in File.ReadAllLines(namesPath, Encoding.UTF8))
            {
                if (line.TrimStart().StartsWith("#")) continue;
                string[] c = line.Split('|');
                long id;
                if (c.Length >= 2 && GameMemory.ParseHex(c[0], out id)) names[(int)id] = c[1].Trim();
            }
        }

        string Describe(int id)
        {
            string n;
            return "0x" + id.ToString("X3") + (names.TryGetValue(id, out n) ? " " + n : "");
        }

        public void Tick()
        {
            if (!main.Attached) return;
            byte[] block = main.Mem.Read(PlayersBase, PlayerSize * PlayerCount);
            if (block == null) return;
            bool ui = (DateTime.Now - lastUi).TotalMilliseconds >= 200;
            if (ui) lastUi = DateTime.Now;
            for (int i = 0; i < PlayerCount; i++)
            {
                int o = i * PlayerSize;
                long anim = BitConverter.ToUInt32(block, o + 4);
                byte[] m = anim > 0x10000 ? main.Mem.Read(anim + 0x30, 2) : null;
                int motion = m == null ? -1 : BitConverter.ToUInt16(m, 0);
                int team = block[o + 0x40], num = block[o + 0x3F];
                if (motion >= 0 && motion != last[i])
                {
                    int c;
                    counts.TryGetValue(motion, out c);
                    if (c == 0)
                    {
                        firstSeen[motion] = DateTime.Now.ToString("HH:mm:ss");
                        main.Log("[Movimiento NUEVO] " + Describe(motion) + "  (jugador " + i + ", equipo " + team + ", núm " + num + ")");
                    }
                    counts[motion] = c + 1;
                    dirty = true;
                }
                last[i] = motion;
                if (ui)
                {
                    ListViewItem it = players.Items[i];
                    float x = BitConverter.ToSingle(block, o + 0x120), z = BitConverter.ToSingle(block, o + 0x128);
                    string[] vals = { i.ToString(), team.ToString(), num.ToString(), motion < 0 ? "-" : Describe(motion),
                                      x.ToString("0") + ", " + z.ToString("0") };
                    for (int k = 1; k < vals.Length; k++)
                        if (it.SubItems[k].Text != vals[k]) it.SubItems[k].Text = vals[k];
                }
            }
            if (dirty && (DateTime.Now - lastSave).TotalSeconds >= 5)
            {
                lastSave = DateTime.Now;
                dirty = false;
                File.WriteAllLines(seenPath, counts.OrderBy(kv => kv.Key)
                    .Select(kv => "0x" + kv.Key.ToString("X3") + "|" + kv.Value + "|" + firstSeen[kv.Key]));
                RefreshSeen();
            }
        }

        void RefreshSeen()
        {
            seen.BeginUpdate();
            seen.Items.Clear();
            foreach (KeyValuePair<int, int> kv in counts.OrderBy(kv => kv.Value))
            {
                string n, f;
                names.TryGetValue(kv.Key, out n);
                firstSeen.TryGetValue(kv.Key, out f);
                seen.Items.Add(new ListViewItem(new string[] { "0x" + kv.Key.ToString("X3"), n ?? "", kv.Value.ToString(), f ?? "" }));
            }
            seen.EndUpdate();
        }
    }

    // Pestaña "Video": resolución interna (backbuffer) del juego. Mismo método que el trainer
    // WE9InternalRes 1.2 (Kingsley813, Cheat Engine): ancho en WE9.exe+A95460 y alto en +A95464.
    class VideoTab : UserControl
    {
        const long ResAddr = 0xE95460; // WE9.exe+A95460
        readonly MainForm main;
        readonly string path;
        readonly Label current = new Label();
        readonly NumericUpDown width = new NumericUpDown();
        readonly NumericUpDown height = new NumericUpDown();
        readonly CheckBox auto = new CheckBox();
        readonly CheckBox ultrawide = new CheckBox();
        bool loading;

        // Ultrawide: además de la resolución interna, kitserver tiene que abrir la ventana/pantalla
        // en 3440x1440 (kload.cfg). Al apagarlo se dejan esas líneas como venían de fábrica.
        static readonly string[] KloadUltrawide = { "dx.fullscreen.width = 3440", "dx.fullscreen.height = 1440",
                                                    "dx.window.width = 3440", "dx.window.height = 1440" };
        static readonly string[] KloadNormal = { "#dx.fullscreen.width = 1920", "#dx.fullscreen.height = 1200" };

        void SetUltrawide(bool on)
        {
            if (on) SetDesired(3440, 1440);
            else SetDesired(2560, 1440);
            string kload = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "kitserver", "kload.cfg");
            if (File.Exists(kload))
            {
                List<string> lines = File.ReadAllLines(kload).ToList();
                int at = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*#?\s*dx\.(fullscreen|window)\.(width|height)\s*="));
                lines.RemoveAll(l => Regex.IsMatch(l, @"^\s*#?\s*dx\.(fullscreen|window)\.(width|height)\s*="));
                if (at < 0) at = lines.FindIndex(l => l.Contains("dx.emulate-HW-TnL")) + 1;
                if (at <= 0) at = lines.Count;
                lines.InsertRange(at, on ? KloadUltrawide : KloadNormal);
                File.WriteAllLines(kload, lines);
                main.Log("Video: kitserver " + (on ? "abrirá el juego en 3440 x 1440 (ultrawide)" : "vuelve a su tamaño de ventana normal") +
                         " desde la próxima vez que se abra el juego");
            }
            Apply();
        }

        public VideoTab(MainForm main, string dir)
        {
            this.main = main;
            path = Path.Combine(dir, "video.txt");
            Padding = new Padding(10);

            current.Dock = DockStyle.Top;
            current.Height = 30;
            current.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            current.Text = "Resolución interna actual: -";

            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.Dock = DockStyle.Top;
            presets.Height = 40;
            presets.Height = 76;
            // 16:9; 3840x2160 en una pantalla 1440p funciona como supersampling (suaviza bordes)
            int[][] sizes = { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } };
            foreach (int[] s in sizes)
            {
                int ww = s[0], hh = s[1];
                presets.Controls.Add(MainForm.MakeButton(ww + " x " + hh, delegate { SetDesired(ww, hh); Apply(); }));
            }
            ultrawide.Text = "Modo ultrawide (21:9, 3440 x 1440)";
            ultrawide.AutoSize = true;
            ultrawide.Margin = new Padding(12, 8, 0, 0);
            ultrawide.CheckedChanged += delegate { if (!loading) SetUltrawide(ultrawide.Checked); };
            presets.Controls.Add(ultrawide);
            presets.Controls.Add(MainForm.MakeButton("Relación de aspecto (LOD Mixer)", delegate { OpenLodMixer(); }));

            FlowLayoutPanel custom = new FlowLayoutPanel();
            custom.Dock = DockStyle.Top;
            custom.Height = 40;
            foreach (NumericUpDown n in new[] { width, height })
            {
                n.Minimum = 64;
                n.Maximum = 8192;
                n.Width = 80;
                n.Margin = new Padding(4, 6, 4, 0);
            }
            // Valor por defecto: 2560x1440 (sirve en cualquier pantalla), aplicado solo al abrir el juego
            width.Value = 2560;
            height.Value = 1440;
            auto.Checked = true;
            custom.Controls.Add(MakeLabel("Ancho:"));
            custom.Controls.Add(width);
            custom.Controls.Add(MakeLabel("Alto:"));
            custom.Controls.Add(height);
            custom.Controls.Add(MainForm.MakeButton("Aplicar", delegate { Apply(); }));

            auto.Dock = DockStyle.Top;
            auto.Height = 30;
            auto.Text = "Aplicar esta resolución automáticamente cada vez que se abre el juego";
            auto.CheckedChanged += delegate { if (!loading) Save(); };

            Label help = new Label();
            help.Dock = DockStyle.Top;
            help.Height = 110;
            help.ForeColor = Color.DimGray;
            help.Text = "Es la resolución a la que el juego dibuja la imagen (más alta = más nítida, más exigente). " +
                        "Una resolución mayor que la de tu pantalla funciona como antialiasing (supersampling). " +
                        "Si no se nota el cambio, entra a un partido nuevo o vuelve al menú.\n" +
                        "Modo ultrawide: solo para pantallas 21:9. Cambia también el tamaño con que kitserver abre el juego " +
                        "(vale desde la próxima vez que lo abras) y conviene activar \"Automatic aspect ratio\" en el LOD Mixer " +
                        "para que no se vea estirado. Apagado, todo queda como en cualquier PC.";

            Controls.Add(help);
            Controls.Add(auto);
            Controls.Add(custom);
            Controls.Add(presets);
            Controls.Add(current);
        }

        static Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(6, 9, 0, 0);
            return l;
        }

        void SetDesired(int w, int h)
        {
            width.Value = w;
            height.Value = h;
        }

        void OpenLodMixer()
        {
            string lodcfg = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "kitserver", "lodcfg.exe");
            if (!File.Exists(lodcfg))
            {
                main.Log("Video: no se encontró kitserver\\lodcfg.exe");
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo(lodcfg);
            psi.WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(lodcfg));
            Process.Start(psi);
        }

        public void LoadSettings()
        {
            loading = true;
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] kv = line.Split('=');
                    int v;
                    if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out v)) continue;
                    switch (kv[0].Trim())
                    {
                        case "ancho": if (v >= 64 && v <= 8192) width.Value = v; break;
                        case "alto": if (v >= 64 && v <= 8192) height.Value = v; break;
                        case "auto": auto.Checked = v == 1; break;
                        case "ultrawide": ultrawide.Checked = v == 1; break;
                    }
                }
            loading = false;
        }

        void Save()
        {
            File.WriteAllText(path, "ancho=" + width.Value + "\r\nalto=" + height.Value + "\r\nauto=" + (auto.Checked ? 1 : 0) +
                                    "\r\nultrawide=" + (ultrawide.Checked ? 1 : 0) + "\r\n");
        }

        void Apply()
        {
            Save();
            if (!main.Attached)
            {
                main.Log("Video: el juego no está abierto; se aplicará al abrirlo si marcas la casilla automática");
                return;
            }
            byte[] data = new byte[8];
            BitConverter.GetBytes((int)width.Value).CopyTo(data, 0);
            BitConverter.GetBytes((int)height.Value).CopyTo(data, 4);
            if (main.Mem.Write(ResAddr, data))
                main.Log("Video: resolución interna = " + width.Value + " x " + height.Value);
            else
                main.Log("Video: no se pudo escribir en el juego (¿abrir el monitor como administrador?)");
        }

        public void Tick()
        {
            if (!main.Attached) return;
            byte[] b = main.Mem.Read(ResAddr, 8);
            if (b == null) return;
            int w = BitConverter.ToInt32(b, 0), h = BitConverter.ToInt32(b, 4);
            string text = "Resolución interna actual: " + w + " x " + h;
            if (current.Text != text) current.Text = text;
            // El juego pone su valor al arrancar; si está la opción automática, se reemplaza una vez.
            if (auto.Checked && w > 0 && h > 0 && (w != (int)width.Value || h != (int)height.Value))
                Apply();
        }
    }

    // Graba sin parar la memoria de datos de WE9.exe (cada 125 ms) y, al apretar una
    // tecla de evento, guarda lo que cambió en los últimos 2 s y NO en los 2 s anteriores.
    // Así se descarta solo lo que cambia todo el tiempo (reloj, posiciones, cámara).
    class Recorder
    {
        public static readonly string[] Labels = { "Elástica", "Remate", "Otra jugada", "Tiro libre" };
        const int IntervalMs = 125;
        const uint Recent = 0x0000FFFF;   // últimas 16 muestras = 2 s
        const uint Before = 0xFFFF0000;   // las 16 anteriores = 2 s

        readonly MainForm main;
        readonly string eventsPath, summaryPath;
        readonly object sync = new object();
        readonly Dictionary<int, int[]> hits = new Dictionary<int, int[]>();
        readonly int[] eventsPerLabel = new int[Labels.Length];
        long start;
        int count, samples, eventNumber;
        uint[] cur, prev, mask;
        readonly uint[][] ring = new uint[3][];
        readonly DateTime[] ringTime = new DateTime[3];
        int ringPos;
        DateTime lastRing = DateTime.MinValue;
        Thread thread;
        volatile bool running;

        public Recorder(MainForm main, string eventsPath, string summaryPath)
        {
            this.main = main;
            this.eventsPath = eventsPath;
            this.summaryPath = summaryPath;
        }

        public void Start()
        {
            long end;
            if (running || !main.Mem.DataSectionRange(out start, out end)) return;
            count = (int)((end - start) / 4);
            cur = new uint[count];
            prev = new uint[count];
            mask = new uint[count];
            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = new uint[count];
                ringTime[i] = DateTime.MinValue;
            }
            samples = 0;
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
            main.Log("Grabación continua activa: " + main.Mem.Describe(start) + " a " + main.Mem.Describe(end) +
                     " (" + (count * 4 / 1048576) + " MB). F9 = Elástica, F10 = Remate, F11 = Otra jugada, F8 = Tiro libre");
        }

        public void Stop()
        {
            running = false;
            if (thread != null) thread.Join(2000);
            thread = null;
        }

        void Loop()
        {
            byte[] buf = new byte[count * 4];
            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            IntPtr basePtr = pin.AddrOfPinnedObject();
            const int chunk = 0x100000;
            try
            {
                while (running)
                {
                    DateTime t0 = DateTime.Now;
                    for (int off = 0; off < buf.Length; off += chunk)
                    {
                        int size = Math.Min(chunk, buf.Length - off);
                        if (main.Mem.ReadInto(start + off, basePtr + off, size)) continue;
                        for (int p = 0; p < size; p += 0x1000) // página por página si falla el bloque
                            main.Mem.ReadInto(start + off + p, basePtr + off + p, Math.Min(0x1000, size - p));
                    }
                    lock (sync)
                    {
                        uint[] tmp = prev;
                        prev = cur;
                        cur = tmp;
                        Buffer.BlockCopy(buf, 0, cur, 0, buf.Length);
                        if (samples > 0)
                            for (int i = 0; i < count; i++)
                                mask[i] = (mask[i] << 1) | (cur[i] != prev[i] ? 1u : 0u);
                        samples++;
                        if ((t0 - lastRing).TotalMilliseconds >= 1000)
                        {
                            lastRing = t0;
                            Array.Copy(cur, ring[ringPos], count);
                            ringTime[ringPos] = t0;
                            ringPos = (ringPos + 1) % ring.Length;
                        }
                    }
                    int wait = IntervalMs - (int)(DateTime.Now - t0).TotalMilliseconds;
                    if (wait > 0) Thread.Sleep(wait);
                }
            }
            finally
            {
                pin.Free();
            }
        }

        public void Mark(int label)
        {
            if (!running) return;
            if (samples < 33)
            {
                main.Log("Grabación: todavía juntando datos, espera unos segundos");
                return;
            }
            int n = ++eventNumber;
            DateTime now = DateTime.Now;
            List<int> idx = new List<int>();
            List<uint> oldVals = new List<uint>(), newVals = new List<uint>();
            List<int> times = new List<int>();
            int changedRecent = 0;
            lock (sync)
            {
                // Foto más vieja que tenga al menos 2 s, para mostrar el valor de antes.
                int old = -1;
                for (int k = 0; k < ring.Length; k++)
                    if ((now - ringTime[k]).TotalSeconds >= 2 && (old < 0 || ringTime[k] > ringTime[old])) old = k;
                for (int i = 0; i < count; i++)
                {
                    uint m = mask[i];
                    if ((m & Recent) == 0) continue;
                    changedRecent++;
                    if ((m & Before) != 0) continue;
                    idx.Add(i);
                    oldVals.Add(old >= 0 ? ring[old][i] : 0);
                    newVals.Add(cur[i]);
                    int c = 0;
                    for (uint r = m & Recent; r != 0; r &= r - 1) c++;
                    times.Add(c);
                }
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                eventsPerLabel[label]++;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== EVENTO " + n + ": " + Labels[label] + " (" + now.ToString("HH:mm:ss.fff") + ") ===  " +
                              changedRecent + " cambiaron en los últimos 2 s, " + idx.Count + " SOLO ahí (antes estaban quietos)");
                sb.AppendLine("dirección        hace ~2s       ahora          veces  int / float");
                for (int k = 0; k < idx.Count; k++)
                {
                    long addr = start + idx[k] * 4L;
                    uint o = oldVals[k], v = newVals[k];
                    float f = BitConverter.ToSingle(BitConverter.GetBytes(v), 0);
                    string fl = (Math.Abs(f) > 1e-4 && Math.Abs(f) < 1e6) ? "  " + f.ToString("0.###", CultureInfo.InvariantCulture) : "";
                    sb.AppendLine(main.Mem.Describe(addr).PadRight(16) + o.ToString("X8") + "  ->  " + v.ToString("X8") +
                                  "   " + times[k].ToString().PadLeft(2) + "x   " + (int)o + " -> " + (int)v + fl);
                    lock (hits)
                    {
                        int[] h;
                        if (!hits.TryGetValue(idx[k], out h))
                        {
                            h = new int[Labels.Length];
                            hits[idx[k]] = h;
                        }
                        h[label]++;
                    }
                }
                sb.AppendLine();
                lock (hits)
                {
                    File.AppendAllText(eventsPath, sb.ToString(), Encoding.UTF8);
                    WriteSummary();
                }
                main.Log("Evento " + n + " (" + Labels[label] + "): " + idx.Count + " valores cambiaron solo en ese momento");
            });
            main.Toast(Labels[label].ToUpperInvariant(), "Evento " + n + " guardado");
        }

        // Para cada tipo de evento: los valores que aparecen en más eventos de ese tipo
        // y en menos de los otros. Los primeros son los mejores candidatos.
        void WriteSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RESUMEN (" + DateTime.Now.ToString("HH:mm:ss") + ")");
            for (int l = 0; l < Labels.Length; l++)
            {
                if (eventsPerLabel[l] == 0) continue;
                sb.AppendLine();
                sb.AppendLine("=== " + Labels[l] + ": " + eventsPerLabel[l] + " eventos ===");
                sb.AppendLine("dirección        en " + Labels[l] + "   en otros");
                List<KeyValuePair<int, int[]>> list = new List<KeyValuePair<int, int[]>>();
                foreach (KeyValuePair<int, int[]> kv in hits)
                    if (kv.Value[l] >= Math.Max(1, eventsPerLabel[l] / 2)) list.Add(kv);
                int lab = l;
                list.Sort(delegate(KeyValuePair<int, int[]> a, KeyValuePair<int, int[]> b)
                {
                    int sa = a.Value[lab] * 2 - Others(a.Value, lab), sb2 = b.Value[lab] * 2 - Others(b.Value, lab);
                    return sb2.CompareTo(sa);
                });
                for (int k = 0; k < Math.Min(150, list.Count); k++)
                {
                    int[] h = list[k].Value;
                    sb.AppendLine(main.Mem.Describe(start + list[k].Key * 4L).PadRight(16) + (h[lab] + "/" + eventsPerLabel[lab]).PadLeft(8) +
                                  "      " + Others(h, lab));
                }
            }
            File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
        }

        static int Others(int[] h, int l)
        {
            int s = 0;
            for (int i = 0; i < h.Length; i++) if (i != l) s += h[i];
            return s;
        }
    }

    class MainForm : Form
    {
        public readonly GameMemory Mem = new GameMemory();
        readonly string watchesPath, logrosPath, logPath, gameDir;
        readonly OverlayForm overlay = new OverlayForm();
        readonly HashSet<string> logros = new HashSet<string>();
        List<Watch> watches = new List<Watch>();

        readonly Label status = new Label();
        readonly ListView list = new ListView();
        readonly TextBox logBox = new TextBox();
        readonly CheckBox showOverlay = new CheckBox();
        readonly TabControl tabs = new TabControl();
        readonly TabPage scanPage = new TabPage("Escáner");
        readonly TabPage hexPage = new TabPage("Hex");
        readonly TabPage skillsPage = new TabPage("Habilidades");
        readonly TabPage movesPage = new TabPage("Jugadas ocultas");
        readonly HiddenMovesTab movesTab;
        readonly TabPage kickPage = new TabPage("Tiro al ángulo");
        readonly FreeKickTab kickTab;
        readonly TabPage videoPage = new TabPage("Video");
        readonly VideoTab videoTab;
        readonly TabPage animPage = new TabPage("Movimientos");
        readonly AnimTab animTab;
        readonly TabPage researchPage = new TabPage("Investigación");
        readonly TabControl research = new TabControl();

        void ShowResearch(TabPage page)
        {
            tabs.SelectedTab = researchPage;
            research.SelectedTab = page;
        }

        bool ResearchShown(TabPage page)
        {
            return tabs.SelectedTab == researchPage && research.SelectedTab == page;
        }
        readonly SplitContainer split = new SplitContainer();
        readonly ScannerTab scanner;
        readonly HexTab hex;
        readonly SkillsTab skillsTab;
        readonly FormsTimer timer = new FormsTimer();
        readonly StreamWriter logFile;
        DateTime lastSearch = DateTime.MinValue, lastUi = DateTime.MinValue, lastHex = DateTime.MinValue, lastScan = DateTime.MinValue;
        IntPtr gameWindow = IntPtr.Zero;

        readonly string eventsPath, summaryPath;
        readonly Recorder recorder;

        public MainForm(string baseDir, string gameDir)
        {
            this.gameDir = gameDir;
            watchesPath = Path.Combine(baseDir, "watches.txt");
            logrosPath = Path.Combine(baseDir, "logros_desbloqueados.txt");
            string logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, "WE9_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
            logFile = new StreamWriter(logPath, false, Encoding.UTF8);
            logFile.AutoFlush = true;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            eventsPath = Path.Combine(logDir, "WE9_" + stamp + "_eventos.txt");
            summaryPath = Path.Combine(logDir, "WE9_" + stamp + "_resumen.txt");
            recorder = new Recorder(this, eventsPath, summaryPath);

            Text = "WE9 Monitor";
            Width = 1060;
            Height = 760;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(20, 20);

            status.Dock = DockStyle.Top;
            status.Height = 28;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(8, 0, 0, 0);
            status.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            // Pestaña de watches
            TabPage watchPage = new TabPage("Watches");
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Top;
            buttons.Height = 36;
            buttons.Padding = new Padding(4);
            buttons.Controls.Add(MakeButton("Recargar watches.txt", delegate { LoadWatches(); }));
            buttons.Controls.Add(MakeButton("Editar watches.txt", delegate { Process.Start("notepad.exe", "\"" + watchesPath + "\""); }));
            buttons.Controls.Add(MakeButton("Abrir carpeta de logs", delegate { Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(logPath) + "\""); }));
            buttons.Controls.Add(MakeButton("Probar aviso", delegate { overlay.AddToast("PRUEBA", "El overlay funciona", true); Log("Aviso de prueba"); }));
            buttons.Controls.Add(MakeButton("Elástica (F9)", delegate { recorder.Mark(0); }));
            buttons.Controls.Add(MakeButton("Remate (F10)", delegate { recorder.Mark(1); }));
            buttons.Controls.Add(MakeButton("Otra (F11)", delegate { recorder.Mark(2); }));
            showOverlay.Text = "Mostrar overlay";
            showOverlay.Checked = true;
            showOverlay.AutoSize = true;
            showOverlay.Margin = new Padding(10, 8, 0, 0);
            buttons.Controls.Add(showOverlay);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Columns.Add("Nombre", 200);
            list.Columns.Add("Dirección", 130);
            list.Columns.Add("Valor", 110);
            list.Columns.Add("Tipo", 60);
            list.Columns.Add("Condición", 90);
            list.Columns.Add("Aviso", 70);
            list.DoubleClick += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                Watch w = (Watch)list.SelectedItems[0].Tag;
                if (w.Readable && w.ResolvedAddr > 0) ShowHex(w.ResolvedAddr);
            };
            watchPage.Controls.Add(list);
            watchPage.Controls.Add(buttons);

            scanner = new ScannerTab(this);
            scanner.Dock = DockStyle.Fill;
            scanPage.Controls.Add(scanner);

            hex = new HexTab(this);
            hex.Dock = DockStyle.Fill;
            hexPage.Controls.Add(hex);

            skillsTab = new SkillsTab(this, Path.Combine(baseDir, "habilidades.txt"));
            skillsTab.Dock = DockStyle.Fill;
            skillsPage.Controls.Add(skillsTab);

            movesTab = new HiddenMovesTab(this, baseDir);
            movesTab.Dock = DockStyle.Fill;
            movesPage.Controls.Add(movesTab);

            kickTab = new FreeKickTab(this, baseDir, delegate { recorder.Mark(3); });
            kickTab.Dock = DockStyle.Fill;
            kickPage.Controls.Add(kickTab);

            videoTab = new VideoTab(this, baseDir);
            videoTab.Dock = DockStyle.Fill;
            videoPage.Controls.Add(videoTab);

            animTab = new AnimTab(this, baseDir);
            animTab.Dock = DockStyle.Fill;
            animPage.Controls.Add(animTab);

            // Arriba solo lo de uso diario; las herramientas de búsqueda van juntas en "Investigación"
            research.Dock = DockStyle.Fill;
            research.TabPages.Add(animPage);
            research.TabPages.Add(kickPage);
            research.TabPages.Add(watchPage);
            research.TabPages.Add(scanPage);
            research.TabPages.Add(hexPage);
            research.TabPages.Add(skillsPage);
            researchPage.Controls.Add(research);

            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(movesPage);
            tabs.TabPages.Add(videoPage);
            tabs.TabPages.Add(researchPage);

            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Font = new Font("Consolas", 9f);
            logBox.BackColor = Color.FromArgb(18, 18, 18);
            logBox.ForeColor = Color.FromArgb(220, 220, 220);

            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.Panel1.Controls.Add(tabs);
            split.Panel2.Controls.Add(logBox);

            Controls.Add(split);
            Controls.Add(status);

            if (File.Exists(logrosPath))
                foreach (string l in File.ReadAllLines(logrosPath, Encoding.UTF8))
                    if (l.Trim().Length > 0) logros.Add(l.Trim());

            timer.Interval = 30;
            timer.Tick += delegate { OnTick(); };
        }

        public static Button MakeButton(string text, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Click += click;
            return b;
        }

        public bool Attached
        {
            get { return Mem.Proc != null; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            split.SplitterDistance = Math.Max(200, split.Height - 190);
            Log("Monitor iniciado. Log: " + logPath);
            LoadWatches();
            skillsTab.LoadSkills();
            overlay.Show();
            overlay.Hide();
            Keys[] keys = { Keys.F9, Keys.F10, Keys.F11, Keys.F8 };
            for (int i = 0; i < keys.Length; i++)
                if (!Native.RegisterHotKey(Handle, i + 1, 0, (int)keys[i]))
                    Log("No se pudo registrar " + keys[i] + " (otro programa la usa)");
            Log("Eventos: F9 = Elástica, F10 = Remate, F11 = Otra jugada, F8 = Tiro libre. Archivos: " + eventsPath + " y " + summaryPath);
            movesTab.LoadAll();
            kickTab.LoadVars();
            videoTab.LoadSettings();
            animTab.LoadAll();
            // Con el juego cerrado, dejar WE9.exe sincronizado con jugadas_extra.txt antes de abrirlo
            if (Process.GetProcessesByName("WE9").Length == 0) movesTab.ApplyToFile();
            LaunchGameIfNeeded();
            timer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) recorder.Mark(m.WParam.ToInt32() - 1); // WM_HOTKEY
            base.WndProc(ref m);
        }

        public void Toast(string title, string text)
        {
            overlay.AddToast(title, text, false);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i);
            Log("Monitor cerrado");
            timer.Stop();
            recorder.Stop();
            Mem.Detach();
            logFile.Dispose();
            base.OnFormClosed(e);
        }

        void LaunchGameIfNeeded()
        {
            if (Process.GetProcessesByName("WE9").Length > 0)
            {
                Log("El juego ya estaba abierto");
                return;
            }
            string exe = Path.Combine(gameDir, "WE9.exe");
            if (!File.Exists(exe))
            {
                Log("No se encontró WE9.exe en " + gameDir);
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = gameDir;
            try
            {
                Process.Start(psi);
                Log("Juego iniciado: " + exe);
            }
            catch (Exception ex)
            {
                Log("No se pudo iniciar el juego: " + ex.Message);
            }
        }

        void LoadWatches()
        {
            watches = Watch.Load(watchesPath, Log);
            list.Items.Clear();
            foreach (Watch w in watches)
            {
                ListViewItem it = new ListViewItem(new string[] { w.Name, "-", "-", w.Type, w.Condition, w.Mode });
                it.Tag = w;
                list.Items.Add(it);
            }
            Log("watches.txt cargado: " + watches.Count + " entradas");
        }

        public void AddWatchLine(string line)
        {
            File.AppendAllText(watchesPath, Environment.NewLine + line, Encoding.UTF8);
            Log("Agregado a watches.txt: " + line);
            LoadWatches();
        }

        public void ShowHex(long addr)
        {
            hex.GoTo(addr);
            hex.View.SelectByte(0);
            ShowResearch(hexPage);
        }

        public void SetSkillsBase(long addr)
        {
            skillsTab.SetBase(addr);
            ShowResearch(skillsPage);
        }

        void OnTick()
        {
            if (Mem.Proc == null || !Mem.Alive)
            {
                if (Mem.Proc != null)
                {
                    Log("El juego se cerró");
                    recorder.Stop();
                    Mem.Detach();
                    if (movesTab.PendingFile)
                    {
                        Thread.Sleep(500); // dar tiempo a que Windows libere WE9.exe
                        movesTab.ApplyToFile();
                    }
                    scanner.Reset();
                    gameWindow = IntPtr.Zero;
                    overlay.Hide();
                }
                if ((DateTime.Now - lastSearch).TotalSeconds >= 1)
                {
                    lastSearch = DateTime.Now;
                    TryAttach();
                }
                SetStatus("Esperando a WE9.exe...", Color.DarkOrange);
                return;
            }

            SetStatus("Conectado a WE9.exe (PID " + Mem.Proc.Id + ")" + (Mem.CanWrite ? "" : "  - solo lectura"), Color.ForestGreen);
            foreach (Watch w in watches) Poll(w);
            UpdateOverlayBounds();
            overlay.Tick();
            animTab.Tick();

            DateTime now = DateTime.Now;
            if ((now - lastUi).TotalMilliseconds >= 200)
            {
                lastUi = now;
                skillsTab.Tick();
                movesTab.Tick();
                kickTab.Tick();
                videoTab.Tick();
                foreach (ListViewItem it in list.Items)
                {
                    Watch w = (Watch)it.Tag;
                    string addr = w.Readable ? Mem.Describe(w.ResolvedAddr) : "sin leer";
                    if (it.SubItems[1].Text != addr) it.SubItems[1].Text = addr;
                    if (it.SubItems[2].Text != w.Display) it.SubItems[2].Text = w.Display;
                }
            }
            if (ResearchShown(hexPage) && (now - lastHex).TotalMilliseconds >= 100)
            {
                lastHex = now;
                hex.Tick();
            }
            if (ResearchShown(scanPage) && (now - lastScan).TotalMilliseconds >= 400)
            {
                lastScan = now;
                scanner.Tick();
            }
        }

        void TryAttach()
        {
            Process[] ps = Process.GetProcessesByName("WE9");
            if (ps.Length == 0) return;
            if (Mem.Attach(ps[0]))
            {
                Log("Conectado al juego (PID " + ps[0].Id + ", base " + Mem.ModuleBase.ToString("X8") +
                    (Mem.CanWrite ? "" : ", solo lectura") + ")");
                recorder.Start();
            }
            else
                Log("No se pudo leer la memoria del juego. Si WE9 corre como administrador, abre el monitor como administrador también.");
        }

        void Poll(Watch w)
        {
            long addr;
            byte[] b = null;
            if (Mem.Resolve(w.Expr, out addr)) b = Mem.Read(addr, Values.SizeOf(w.Type));
            if (b == null)
            {
                if (w.Readable) Log("[" + w.Name + "] no se puede leer la dirección (" + w.Expr + ")");
                w.Readable = false;
                w.HasLast = false;
                w.Display = "-";
                return;
            }
            if (!w.Readable) Log("[" + w.Name + "] dirección disponible: " + Mem.Describe(addr));
            w.Readable = true;
            w.ResolvedAddr = addr;

            double v = Values.Decode(w.Type, b, 0);
            w.Display = Values.Format(w.Type, v);
            if (!w.HasLast)
            {
                w.HasLast = true;
                w.Last = v;
                return;
            }
            if (v == w.Last) return;

            double prev = w.Last;
            w.Last = v;
            if (!w.Triggers(prev, v)) return;

            string msg = w.Message
                .Replace("{valor}", Values.Format(w.Type, v))
                .Replace("{anterior}", Values.Format(w.Type, prev))
                .Replace("{nombre}", w.Name);
            Log("[" + w.Name + "] " + Values.Format(w.Type, prev) + " -> " + Values.Format(w.Type, v) + "  " + msg);

            if (w.Mode == "popup")
            {
                overlay.AddToast(w.Name.ToUpperInvariant(), msg, false);
            }
            else if (w.Mode == "logro" && !logros.Contains(w.Name))
            {
                logros.Add(w.Name);
                File.AppendAllText(logrosPath, w.Name + Environment.NewLine, Encoding.UTF8);
                overlay.AddToast("LOGRO DESBLOQUEADO", msg, true);
                Log("LOGRO DESBLOQUEADO: " + w.Name);
            }
        }

        void UpdateOverlayBounds()
        {
            if (gameWindow == IntPtr.Zero || !Native.IsWindow(gameWindow))
            {
                Mem.Proc.Refresh();
                gameWindow = Mem.Proc.MainWindowHandle;
                if (gameWindow != IntPtr.Zero) Log("Ventana del juego encontrada");
            }
            if (!showOverlay.Checked || gameWindow == IntPtr.Zero || Native.IsIconic(gameWindow))
            {
                if (overlay.Visible) overlay.Hide();
                return;
            }
            Native.RECT r;
            Native.GetClientRect(gameWindow, out r);
            Native.POINT p = new Native.POINT();
            Native.ClientToScreen(gameWindow, ref p);
            Rectangle bounds = new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (overlay.Bounds != bounds) overlay.Bounds = bounds;
            if (!overlay.Visible) overlay.Show();
        }

        void SetStatus(string text, Color color)
        {
            if (status.Text == text) return;
            status.Text = text;
            status.ForeColor = color;
        }

        public void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), text);
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text;
            logFile.WriteLine(line);
            if (logBox.TextLength > 200000) logBox.Text = logBox.Text.Substring(100000);
            logBox.AppendText(line.Replace("\n", Environment.NewLine).Replace("\r\r", "\r") + Environment.NewLine);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string gameDir = baseDir;
            if (!File.Exists(Path.Combine(gameDir, "WE9.exe")))
            {
                DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\'));
                if (parent != null) gameDir = parent.FullName;
            }
            Application.EnableVisualStyles();
            Application.Run(new MainForm(baseDir, gameDir));
        }
    }
}
