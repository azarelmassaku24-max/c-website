// ============================================================================
//  C# Quest runtime
//  Dit bestand wordt NIET in de server gecompileerd. Het wordt als resource
//  meegeleverd en samen met de code van de leerling gecompileerd.
//  De SandboxRewriter leidt Console-, Thread.Sleep-, Random- en Environment-
//  aanroepen van de leerling om naar de klassen hieronder. Daardoor:
//    - werken Console.Clear, SetCursorPosition, kleuren en ReadKey ook al is
//      de uitvoer omgeleid (we sturen ANSI-codes naar de browserterminal);
//    - worden tests deterministisch (vaste Random-seed, Sleep wordt overgeslagen);
//    - blijven bestandsbewerkingen binnen de sandbox-map;
//    - krijgen we gestructureerde informatie over exceptions en traces.
// ============================================================================
#nullable disable
#pragma warning disable
namespace QuestRuntime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;

    internal static class QBoot
    {
        internal static bool TestMode;
        internal static string SandboxRoot;

        [ModuleInitializer]
        internal static void Init()
        {
            TestMode = System.Environment.GetEnvironmentVariable("QUEST_MODE") == "test";
            SandboxRoot = System.Environment.GetEnvironmentVariable("QUEST_SANDBOX") ?? System.Environment.CurrentDirectory;
            string culture = System.Environment.GetEnvironmentVariable("QUEST_CULTURE") ?? "nl-BE";
            try
            {
                var ci = CultureInfo.GetCultureInfo(culture);
                CultureInfo.DefaultThreadCurrentCulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
                CultureInfo.CurrentCulture = ci;
                CultureInfo.CurrentUICulture = ci;
            }
            catch { }
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    QConsole.Flush();
                    QTrace.Exception(e.ExceptionObject as Exception);
                    QTrace.Flush();
                    QReport.Exception(e.ExceptionObject as Exception);
                }
                catch { }
                System.Environment.Exit(QReport.ExceptionExitCode);
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { QConsole.Flush(); QTrace.Flush(); } catch { }
            };
            QConsole.Init();
        }
    }

    // ------------------------------------------------------------------ report
    internal static class QReport
    {
        internal const int ExceptionExitCode = 86;
        internal const int InputExhaustedExitCode = 87;
        static readonly object Gate = new object();

        static void Append(string json)
        {
            string path = System.Environment.GetEnvironmentVariable("QUEST_REPORT");
            if (string.IsNullOrEmpty(path)) return;
            lock (Gate) { File.AppendAllText(path, json + "\n"); }
        }

        internal static void Exception(Exception ex)
        {
            if (ex == null) return;
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"exception\",\"exType\":").Append(Json.Str(ex.GetType().FullName));
            sb.Append(",\"message\":").Append(Json.Str(ex.Message));
            if (ex.InnerException != null)
                sb.Append(",\"inner\":").Append(Json.Str(ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message));
            sb.Append(",\"frames\":[");
            bool first = true;
            try
            {
                var st = new System.Diagnostics.StackTrace(ex, true);
                foreach (var f in st.GetFrames() ?? new System.Diagnostics.StackFrame[0])
                {
                    var m = f.GetMethod();
                    if (m == null) continue;
                    string type = m.DeclaringType?.FullName ?? "";
                    if (type.StartsWith("QuestRuntime.")) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"method\":").Append(Json.Str((m.DeclaringType?.Name ?? "") + "." + m.Name));
                    sb.Append(",\"file\":").Append(Json.Str(Path.GetFileName(f.GetFileName() ?? "")));
                    sb.Append(",\"line\":").Append(f.GetFileLineNumber());
                    sb.Append(",\"user\":").Append(f.GetFileLineNumber() > 0 ? "true" : "false").Append('}');
                }
            }
            catch { }
            sb.Append("]}");
            Append(sb.ToString());
        }

        internal static void InputExhausted(int linesRead)
        {
            Append("{\"type\":\"inputExhausted\",\"linesRead\":" + linesRead + "}");
        }

        internal static void Check(string label, bool passed, string expected, string actual, string note)
        {
            Append("{\"type\":\"check\",\"label\":" + Json.Str(label) + ",\"passed\":" + (passed ? "true" : "false")
                + ",\"expected\":" + Json.Str(expected) + ",\"actual\":" + Json.Str(actual) + ",\"note\":" + Json.Str(note) + "}");
        }
    }

    internal static class Json
    {
        internal static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    // ----------------------------------------------------------------- console
    internal static class QConsole
    {
        static Stream _stdout;
        static StreamWriter _out;
        static StreamWriter _err;
        static QOutWriter _outWriter;
        static QInReader _inReader;
        static readonly object OutGate = new object();
        static readonly Queue<int> Input = new Queue<int>();
        static readonly object InGate = new object();
        static bool _eof;
        static int _linesRead;
        static readonly Stack<StringBuilder> Captures = new Stack<StringBuilder>();

        static int _col, _row;
        static ConsoleColor _fg = ConsoleColor.Gray, _bg = ConsoleColor.Black;
        static bool _cursorVisible = true;
        static string _title = "";
        static int _cols = 100, _rows = 30;

        internal static void Init()
        {
            _stdout = System.Console.OpenStandardOutput();
            var enc = new UTF8Encoding(false);
            _out = new StreamWriter(_stdout, enc, 4096) { AutoFlush = !QBoot.TestMode };
            _err = new StreamWriter(System.Console.OpenStandardError(), enc, 1024) { AutoFlush = true };
            _outWriter = new QOutWriter();
            _inReader = new QInReader();
            int.TryParse(System.Environment.GetEnvironmentVariable("QUEST_COLS"), out _cols);
            int.TryParse(System.Environment.GetEnvironmentVariable("QUEST_ROWS"), out _rows);
            if (_cols <= 0) _cols = 100;
            if (_rows <= 0) _rows = 30;
            var reader = new Thread(ReadLoop) { IsBackground = true, Name = "quest-stdin" };
            reader.Start();
        }

        static void ReadLoop()
        {
            try
            {
                var stdin = System.Console.OpenStandardInput();
                var decoder = new UTF8Encoding(false).GetDecoder();
                var bytes = new byte[1024];
                var chars = new char[1024];
                while (true)
                {
                    int n = stdin.Read(bytes, 0, bytes.Length);
                    if (n <= 0) break;
                    int c = decoder.GetChars(bytes, 0, n, chars, 0);
                    lock (InGate)
                    {
                        for (int i = 0; i < c; i++) Input.Enqueue(chars[i]);
                        Monitor.PulseAll(InGate);
                    }
                }
            }
            catch { }
            lock (InGate) { _eof = true; Monitor.PulseAll(InGate); }
        }

        static int NextChar()
        {
            lock (InGate)
            {
                while (Input.Count == 0 && !_eof) Monitor.Wait(InGate);
                if (Input.Count == 0) return -1;
                return Input.Dequeue();
            }
        }

        static int PeekChar(int waitMs)
        {
            lock (InGate)
            {
                if (Input.Count == 0 && !_eof && waitMs > 0) Monitor.Wait(InGate, waitMs);
                return Input.Count == 0 ? -1 : Input.Peek();
            }
        }

        static void Exhausted()
        {
            Flush();
            QReport.InputExhausted(_linesRead);
            QTrace.Flush();
            System.Environment.Exit(QReport.InputExhaustedExitCode);
        }

        // ---------------- raw output with cursor tracking
        internal static void Raw(string s)
        {
            if (s == null || s.Length == 0) return;
            if (Captures.Count > 0) { Captures.Peek().Append(s); return; }
            lock (OutGate)
            {
                _out.Write(s);
                Track(s);
            }
            QTrace.Output(s);
        }

        static void Control(string ansi)
        {
            if (Captures.Count > 0) return;
            lock (OutGate) { _out.Write(ansi); }
        }

        static void Track(string s)
        {
            foreach (char c in s)
            {
                if (c == '\n') { _row++; _col = 0; }
                else if (c == '\r') _col = 0;
                else if (c == '\b') { if (_col > 0) _col--; }
                else if (c == '\a') { }
                else
                {
                    _col++;
                    if (_col >= _cols) { _col = 0; _row++; }
                }
            }
        }

        internal static void Flush()
        {
            try { lock (OutGate) { _out?.Flush(); } } catch { }
        }

        internal static void BeginCapture() { Captures.Push(new StringBuilder()); }
        internal static string EndCapture() { return Captures.Count > 0 ? Captures.Pop().ToString() : ""; }

        static string F(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.CurrentCulture); }

        // ---------------- Write / WriteLine
        public static void Write(string value) { Raw(value); }
        public static void Write(object value) { Raw(F(value)); }
        public static void Write(bool value) { Raw(value.ToString()); }
        public static void Write(char value) { Raw(value.ToString()); }
        public static void Write(char[] buffer) { if (buffer != null) Raw(new string(buffer)); }
        public static void Write(char[] buffer, int index, int count) { Raw(new string(buffer, index, count)); }
        public static void Write(decimal value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(double value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(float value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(int value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(uint value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(long value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(ulong value) { Raw(value.ToString(CultureInfo.CurrentCulture)); }
        public static void Write(string format, object arg0) { Raw(string.Format(format, arg0)); }
        public static void Write(string format, object arg0, object arg1) { Raw(string.Format(format, arg0, arg1)); }
        public static void Write(string format, object arg0, object arg1, object arg2) { Raw(string.Format(format, arg0, arg1, arg2)); }
        public static void Write(string format, params object[] arg) { Raw(string.Format(format, arg)); }

        public static void WriteLine() { Raw("\n"); }
        public static void WriteLine(string value) { Raw(value + "\n"); }
        public static void WriteLine(object value) { Raw(F(value) + "\n"); }
        public static void WriteLine(bool value) { Raw(value + "\n"); }
        public static void WriteLine(char value) { Raw(value + "\n"); }
        public static void WriteLine(char[] buffer) { Raw((buffer == null ? "" : new string(buffer)) + "\n"); }
        public static void WriteLine(char[] buffer, int index, int count) { Raw(new string(buffer, index, count) + "\n"); }
        public static void WriteLine(decimal value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(double value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(float value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(int value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(uint value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(long value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(ulong value) { Raw(value.ToString(CultureInfo.CurrentCulture) + "\n"); }
        public static void WriteLine(string format, object arg0) { Raw(string.Format(format, arg0) + "\n"); }
        public static void WriteLine(string format, object arg0, object arg1) { Raw(string.Format(format, arg0, arg1) + "\n"); }
        public static void WriteLine(string format, object arg0, object arg1, object arg2) { Raw(string.Format(format, arg0, arg1, arg2) + "\n"); }
        public static void WriteLine(string format, params object[] arg) { Raw(string.Format(format, arg) + "\n"); }

        // ---------------- input
        public static string ReadLine()
        {
            Flush();
            var sb = new StringBuilder();
            while (true)
            {
                int c = NextChar();
                if (c == -1)
                {
                    if (sb.Length == 0) Exhausted();
                    break;
                }
                if (c == '\r' || c == '\n') break;
                if (c == 127 || c == '\b')
                {
                    if (sb.Length > 0) { sb.Length--; EchoErase(); }
                    continue;
                }
                if (c == 27) { SkipEscape(); continue; }
                if (c == 3) { Flush(); System.Environment.Exit(130); }
                if (c < 32 && c != '\t') continue;
                sb.Append((char)c);
                Raw(((char)c).ToString());
            }
            Raw("\n");
            _linesRead++;
            QTrace.Input(sb.ToString());
            return sb.ToString();
        }

        static void EchoErase()
        {
            if (Captures.Count > 0) return;
            lock (OutGate) { _out.Write("\b \b"); if (_col > 0) _col--; }
        }

        static void SkipEscape()
        {
            int next = PeekChar(30);
            if (next == '[' || next == 'O')
            {
                NextChar();
                while (true)
                {
                    int c = PeekChar(30);
                    if (c == -1) break;
                    NextChar();
                    if (c >= 0x40 && c <= 0x7E) break;
                }
            }
        }

        public static int Read()
        {
            Flush();
            int c = NextChar();
            if (c == -1) Exhausted();
            if (c == '\r') c = '\n';
            Raw(((char)c).ToString());
            return c;
        }

        public static ConsoleKeyInfo ReadKey() { return ReadKey(false); }

        public static ConsoleKeyInfo ReadKey(bool intercept)
        {
            Flush();
            int c = NextChar();
            if (c == -1) Exhausted();
            ConsoleKeyInfo info;
            if (c == 27)
            {
                int n = PeekChar(30);
                if (n == '[' || n == 'O')
                {
                    NextChar();
                    int code = NextChar();
                    ConsoleKey key = ConsoleKey.Escape;
                    switch (code)
                    {
                        case 'A': key = ConsoleKey.UpArrow; break;
                        case 'B': key = ConsoleKey.DownArrow; break;
                        case 'C': key = ConsoleKey.RightArrow; break;
                        case 'D': key = ConsoleKey.LeftArrow; break;
                        case 'H': key = ConsoleKey.Home; break;
                        case 'F': key = ConsoleKey.End; break;
                        default:
                            // bv. ESC [ 3 ~ (Delete)
                            if (code >= '0' && code <= '9')
                            {
                                int t = NextChar();
                                if (code == '3') key = ConsoleKey.Delete;
                                while (t != -1 && t != '~') t = NextChar();
                            }
                            break;
                    }
                    info = new ConsoleKeyInfo('\0', key, false, false, false);
                }
                else info = new ConsoleKeyInfo((char)27, ConsoleKey.Escape, false, false, false);
            }
            else
            {
                char ch = (char)c;
                if (ch == '\n') ch = '\r';
                info = new ConsoleKeyInfo(ch, MapKey(ch), char.IsUpper(ch), false, ch < 27 && ch != '\r' && ch != '\t' && ch != '\b');
                if (!intercept && ch >= 32 && ch != 127) Raw(ch.ToString());
                else if (!intercept && ch == '\r') Raw("\n");
            }
            QTrace.Input("[toets " + info.Key + "]");
            return info;
        }

        static ConsoleKey MapKey(char ch)
        {
            if (ch >= 'a' && ch <= 'z') return (ConsoleKey)('A' + (ch - 'a'));
            if (ch >= 'A' && ch <= 'Z') return (ConsoleKey)ch;
            if (ch >= '0' && ch <= '9') return (ConsoleKey)ch;
            switch (ch)
            {
                case '\r': return ConsoleKey.Enter;
                case ' ': return ConsoleKey.Spacebar;
                case '\t': return ConsoleKey.Tab;
                case (char)127:
                case '\b': return ConsoleKey.Backspace;
                case '+': return ConsoleKey.OemPlus;
                case '-': return ConsoleKey.OemMinus;
                case ',': return ConsoleKey.OemComma;
                case '.': return ConsoleKey.OemPeriod;
            }
            return (ConsoleKey)0;
        }

        public static bool KeyAvailable
        {
            get
            {
                Flush();
                if (!QBoot.TestMode) Thread.Sleep(1);
                lock (InGate) { return Input.Count > 0; }
            }
        }

        // ---------------- screen control
        public static void Clear()
        {
            Control("\u001b[2J\u001b[3J\u001b[H");
            if (Captures.Count == 0) { _col = 0; _row = 0; }
            QTrace.Output("\f");
        }

        public static void SetCursorPosition(int left, int top)
        {
            if (left < 0 || top < 0)
                throw new ArgumentOutOfRangeException(left < 0 ? "left" : "top", "De positie moet groter dan of gelijk aan nul zijn.");
            Control("\u001b[" + (top + 1) + ";" + (left + 1) + "H");
            _col = left; _row = top;
        }

        public static (int Left, int Top) GetCursorPosition() { return (_col, _row); }

        public static int CursorLeft
        {
            get { return _col; }
            set { SetCursorPosition(value, _row); }
        }

        public static int CursorTop
        {
            get { return _row; }
            set { SetCursorPosition(_col, value); }
        }

        public static bool CursorVisible
        {
            get { return _cursorVisible; }
            set { _cursorVisible = value; Control(value ? "\u001b[?25h" : "\u001b[?25l"); }
        }

        public static int CursorSize { get { return 25; } set { } }

        static int Ansi(ConsoleColor c)
        {
            switch (c)
            {
                case ConsoleColor.Black: return 30;
                case ConsoleColor.DarkBlue: return 34;
                case ConsoleColor.DarkGreen: return 32;
                case ConsoleColor.DarkCyan: return 36;
                case ConsoleColor.DarkRed: return 31;
                case ConsoleColor.DarkMagenta: return 35;
                case ConsoleColor.DarkYellow: return 33;
                case ConsoleColor.Gray: return 37;
                case ConsoleColor.DarkGray: return 90;
                case ConsoleColor.Blue: return 94;
                case ConsoleColor.Green: return 92;
                case ConsoleColor.Cyan: return 96;
                case ConsoleColor.Red: return 91;
                case ConsoleColor.Magenta: return 95;
                case ConsoleColor.Yellow: return 93;
                default: return 97;
            }
        }

        public static ConsoleColor ForegroundColor
        {
            get { return _fg; }
            set { _fg = value; Control("\u001b[" + Ansi(value) + "m"); }
        }

        public static ConsoleColor BackgroundColor
        {
            get { return _bg; }
            set { _bg = value; Control("\u001b[" + (Ansi(value) + 10) + "m"); }
        }

        public static void ResetColor()
        {
            _fg = ConsoleColor.Gray; _bg = ConsoleColor.Black;
            Control("\u001b[0m");
        }

        public static void Beep() { Control("\u0007"); }
        public static void Beep(int frequency, int duration) { Control("\u0007"); QThread.Sleep(duration); }

        public static string Title { get { return _title; } set { _title = value ?? ""; } }
        public static int WindowWidth { get { return _cols; } set { } }
        public static int WindowHeight { get { return _rows; } set { } }
        public static int BufferWidth { get { return _cols; } set { } }
        public static int BufferHeight { get { return 9000; } set { } }
        public static int LargestWindowWidth { get { return _cols; } }
        public static int LargestWindowHeight { get { return _rows; } }
        public static int WindowLeft { get { return 0; } set { } }
        public static int WindowTop { get { return 0; } set { } }
        public static void SetWindowSize(int width, int height) { }
        public static void SetBufferSize(int width, int height) { }
        public static bool TreatControlCAsInput { get { return false; } set { } }
        public static bool IsOutputRedirected { get { return false; } }
        public static bool IsInputRedirected { get { return false; } }
        public static bool IsErrorRedirected { get { return false; } }
        public static Encoding OutputEncoding { get { return Encoding.UTF8; } set { } }
        public static Encoding InputEncoding { get { return Encoding.UTF8; } set { } }

        public static TextWriter Out { get { return _outWriter; } }
        public static TextWriter Error { get { return new QErrWriter(_err); } }
        public static TextReader In { get { return _inReader; } }

        sealed class QOutWriter : TextWriter
        {
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override IFormatProvider FormatProvider { get { return CultureInfo.CurrentCulture; } }
            public override void Write(char value) { Raw(value.ToString()); }
            public override void Write(string value) { Raw(value); }
            public override void WriteLine(string value) { Raw(value + "\n"); }
            public override void WriteLine() { Raw("\n"); }
            public override void Flush() { QConsole.Flush(); }
        }

        sealed class QErrWriter : TextWriter
        {
            readonly StreamWriter _w;
            public QErrWriter(StreamWriter w) { _w = w; }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override void Write(char value) { QConsole.Flush(); _w.Write(value); }
            public override void Write(string value) { QConsole.Flush(); _w.Write(value); }
        }

        sealed class QInReader : TextReader
        {
            public override string ReadLine() { return QConsole.ReadLine(); }
            public override int Read() { return QConsole.Read(); }
        }
    }

    // ------------------------------------------------------------------ thread
    internal static class QThread
    {
        internal static long VirtualMs;
        public static void Sleep(int millisecondsTimeout)
        {
            QConsole.Flush();
            QTrace.Sleep(millisecondsTimeout);
            if (millisecondsTimeout <= 0) return;
            VirtualMs += millisecondsTimeout;
            if (QBoot.TestMode) return;
            Thread.Sleep(Math.Min(millisecondsTimeout, 60000));
        }
        public static void Sleep(TimeSpan timeout) { Sleep((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)); }
    }

    // ------------------------------------------------------------------ random
    internal static class QRandom
    {
        static int _counter;
        static Random _shared;
        static int? Seed()
        {
            int s;
            return int.TryParse(System.Environment.GetEnvironmentVariable("QUEST_SEED"), out s) ? s : (int?)null;
        }
        public static Random Create()
        {
            int? s = Seed();
            return s.HasValue ? new Random(s.Value + 7919 * _counter++) : new Random();
        }
        public static Random Create(int seed) { return new Random(seed); }
        public static Random Shared
        {
            get
            {
                if (_shared == null)
                {
                    int? s = Seed();
                    _shared = s.HasValue ? new Random(s.Value ^ 0x5EED) : new Random();
                }
                return _shared;
            }
        }
    }

    // ------------------------------------------------------------- environment
    internal static class QEnvironment
    {
        public static string GetFolderPath(System.Environment.SpecialFolder folder)
        {
            string dir = Path.Combine(QBoot.SandboxRoot, folder.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }
        public static string GetFolderPath(System.Environment.SpecialFolder folder, System.Environment.SpecialFolderOption option)
        {
            return GetFolderPath(folder);
        }
        public static string GetEnvironmentVariable(string variable) { return null; }
        public static string GetEnvironmentVariable(string variable, EnvironmentVariableTarget target) { return null; }
        public static IDictionary GetEnvironmentVariables() { return new Hashtable(); }
        public static IDictionary GetEnvironmentVariables(EnvironmentVariableTarget target) { return new Hashtable(); }
        public static void SetEnvironmentVariable(string variable, string value) { }
        public static string UserName { get { return "student"; } }
        public static string MachineName { get { return "SANDBOX"; } }
        public static string UserDomainName { get { return "SANDBOX"; } }
        public static string CurrentDirectory
        {
            get { return System.Environment.CurrentDirectory; }
            set { throw new UnauthorizedAccessException("In de C# Quest sandbox kan je de huidige map niet wijzigen."); }
        }
        public static string[] GetCommandLineArgs() { return new[] { "Program" }; }
        public static string CommandLine { get { return "Program"; } }
        public static string[] GetLogicalDrives() { return new[] { Path.GetPathRoot(QBoot.SandboxRoot) }; }
    }

    // --------------------------------------------------------------- path guard
    internal static class QPath
    {
        public static string Guard(string path)
        {
            if (path == null) return null;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return path; }
            string root = Path.GetFullPath(QBoot.SandboxRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool inside = full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!inside)
                throw new UnauthorizedAccessException("Toegang geweigerd tot '" + path + "'. In de C# Quest sandbox kan je enkel bestanden gebruiken in de map van je programma (bv. \"personen.csv\" of een pad gemaakt met Environment.GetFolderPath).");
            return path;
        }
    }

    // ----------------------------------------------------------------- checks
    /// <summary>Hulpmiddelen voor automatische tests die methods van de leerling oproepen.</summary>
    internal static class Check
    {
        internal static int Failures;

        public static string Format(object o) { return QFormat.Plain(o); }

        public static void Equal(object expected, object actual, string label)
        {
            bool ok = AreEqual(expected, actual);
            if (!ok) Failures++;
            QReport.Check(label, ok, Format(expected), Format(actual), null);
        }

        public static void True(bool condition, string label)
        {
            if (!condition) Failures++;
            QReport.Check(label, condition, "true", condition ? "true" : "false", null);
        }

        public static void Throws<T>(Action action, string label) where T : Exception
        {
            try
            {
                action();
                Failures++;
                QReport.Check(label, false, "exception " + typeof(T).Name, "geen exception", null);
            }
            catch (T)
            {
                QReport.Check(label, true, "exception " + typeof(T).Name, "exception " + typeof(T).Name, null);
            }
            catch (Exception ex)
            {
                Failures++;
                QReport.Check(label, false, "exception " + typeof(T).Name, "exception " + ex.GetType().Name, null);
            }
        }

        /// <summary>Voert de actie uit en geeft terug wat er op de console werd geschreven.</summary>
        public static string Output(Action action)
        {
            QConsole.BeginCapture();
            try { action(); }
            finally { }
            return QConsole.EndCapture();
        }

        public static void OutputEqual(string expected, Action action, string label)
        {
            string actual;
            QConsole.BeginCapture();
            try { action(); }
            finally { actual = QConsole.EndCapture(); }
            bool ok = Normalize(expected) == Normalize(actual);
            if (!ok) Failures++;
            QReport.Check(label, ok, expected, actual, "output");
        }

        static string Normalize(string s)
        {
            if (s == null) return "";
            var lines = s.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
            return string.Join("\n", lines).TrimEnd('\n');
        }

        static bool AreEqual(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a) && IsNumber(b))
            {
                double x = Convert.ToDouble(a), y = Convert.ToDouble(b);
                return Math.Abs(x - y) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
            }
            if (a is string || b is string) return Equals(a.ToString(), b.ToString());
            if (a is IEnumerable ea && b is IEnumerable eb)
            {
                var la = new List<object>(); foreach (var o in ea) la.Add(o);
                var lb = new List<object>(); foreach (var o in eb) lb.Add(o);
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++) if (!AreEqual(la[i], lb[i])) return false;
                return true;
            }
            return a.Equals(b);
        }

        static bool IsNumber(object o)
        {
            return o is int || o is long || o is double || o is float || o is decimal || o is short || o is byte || o is uint || o is ulong;
        }
    }

    // ---------------------------------------------------------------- format
    internal static class QFormat
    {
        static readonly ConditionalWeakTable<object, object> Ids = new ConditionalWeakTable<object, object>();
        static int _nextId = 1;

        internal static int IdOf(object o)
        {
            object id;
            if (!Ids.TryGetValue(o, out id)) { id = _nextId++; Ids.Add(o, id); }
            return (int)id;
        }

        static bool IsSimple(object o)
        {
            return o is string || o is char || o is bool || o is Enum || o.GetType().IsPrimitive || o is decimal || o is DateTime || o is TimeSpan;
        }

        internal static string Plain(object o)
        {
            if (o == null) return "null";
            if (o is string s) return s;
            if (o is IEnumerable e && !(o is string))
            {
                var parts = new List<string>();
                foreach (var x in e) { parts.Add(Plain(x)); if (parts.Count > 50) { parts.Add("..."); break; } }
                return "[" + string.Join(", ", parts) + "]";
            }
            return Convert.ToString(o, CultureInfo.CurrentCulture);
        }

        internal static string Value(object o, int depth)
        {
            if (o == null) return "null";
            if (o is string s) return "\"" + (s.Length > 200 ? s.Substring(0, 200) + "…" : s) + "\"";
            if (o is char c) return "'" + c + "'";
            if (o is bool b) return b ? "true" : "false";
            if (IsSimple(o)) return Convert.ToString(o, CultureInfo.CurrentCulture);
            var type = o.GetType();
            if (o is IDictionary d)
            {
                var parts = new List<string>();
                foreach (DictionaryEntry kv in d)
                {
                    parts.Add(Value(kv.Key, depth + 1) + ": " + Value(kv.Value, depth + 1));
                    if (parts.Count >= 20) { parts.Add("…"); break; }
                }
                return "#" + IdOf(o) + " {" + string.Join(", ", parts) + "}";
            }
            if (o is IEnumerable en)
            {
                if (depth > 2) return "#" + IdOf(o) + " [...]";
                var parts = new List<string>();
                foreach (var x in en)
                {
                    parts.Add(Value(x, depth + 1));
                    if (parts.Count >= 30) { parts.Add("…"); break; }
                }
                return "#" + IdOf(o) + " [" + string.Join(", ", parts) + "]";
            }
            if (type.Assembly == typeof(QFormat).Assembly && !type.FullName.StartsWith("QuestRuntime"))
            {
                if (depth > 1) return "#" + IdOf(o) + " " + type.Name;
                var parts = new List<string>();
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    string name = f.Name;
                    if (name.StartsWith("<") && name.Contains(">k__BackingField")) name = name.Substring(1, name.IndexOf('>') - 1);
                    object v;
                    try { v = f.GetValue(o); } catch { v = "?"; }
                    parts.Add(name + "=" + Value(v, depth + 1));
                    if (parts.Count >= 12) break;
                }
                return "#" + IdOf(o) + " " + type.Name + " {" + string.Join(", ", parts) + "}";
            }
            return Convert.ToString(o, CultureInfo.CurrentCulture);
        }

        internal static string TypeName(object o)
        {
            if (o == null) return "null";
            var t = o.GetType();
            if (t == typeof(int)) return "int";
            if (t == typeof(double)) return "double";
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(char)) return "char";
            if (t == typeof(decimal)) return "decimal";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t.IsArray) return TypeName2(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                string n = t.Name.Substring(0, t.Name.IndexOf('`'));
                var args = new List<string>();
                foreach (var a in t.GetGenericArguments()) args.Add(TypeName2(a));
                return n + "<" + string.Join(", ", args) + ">";
            }
            return t.Name;
        }

        static string TypeName2(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(double)) return "double";
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(char)) return "char";
            if (t == typeof(decimal)) return "decimal";
            return t.Name;
        }
    }

    // ----------------------------------------------------------------- tracer
    /// <summary>Verzamelt uitvoeringsstappen voor traceertabellen en de call stack-weergave.</summary>
    internal static class QTrace
    {
        static readonly bool Enabled = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QUEST_TRACE"));
        static readonly int Max = 20000;
        static readonly StringBuilder Buffer = new StringBuilder();
        static int _count;
        static int _depth;
        static bool _truncated;
        static readonly StringBuilder PendingOut = new StringBuilder();

        static bool Add(string json)
        {
            if (!Enabled) return false;
            if (_count >= Max)
            {
                if (!_truncated) { _truncated = true; Buffer.Append("{\"k\":\"T\"}\n"); }
                return false;
            }
            FlushOut();
            _count++;
            Buffer.Append(json).Append('\n');
            if (Buffer.Length > 1 << 20) Flush();
            return true;
        }

        static void FlushOut()
        {
            if (PendingOut.Length == 0) return;
            string s = PendingOut.ToString();
            PendingOut.Clear();
            _count++;
            Buffer.Append("{\"k\":\"O\",\"s\":").Append(Json.Str(s)).Append("}\n");
        }

        internal static void Flush()
        {
            if (!Enabled) return;
            FlushOut();
            if (Buffer.Length == 0) return;
            File.AppendAllText(System.Environment.GetEnvironmentVariable("QUEST_TRACE"), Buffer.ToString());
            Buffer.Clear();
        }

        internal static void Output(string s)
        {
            if (!Enabled || _truncated) return;
            PendingOut.Append(s);
        }

        internal static void Input(string s)
        {
            if (!Enabled) return;
            Add("{\"k\":\"I\",\"s\":" + Json.Str(s) + ",\"d\":" + _depth + "}");
        }

        internal static void Sleep(int ms)
        {
            if (!Enabled) return;
            Add("{\"k\":\"S\",\"ms\":" + ms + ",\"d\":" + _depth + "}");
        }

        internal static void Exception(Exception ex)
        {
            if (!Enabled || ex == null) return;
            Add("{\"k\":\"Z\",\"t\":" + Json.Str(ex.GetType().Name) + ",\"m\":" + Json.Str(ex.Message) + ",\"d\":" + _depth + "}");
        }

        static string Var(string name, object value)
        {
            string v;
            try { v = QFormat.Value(value, 0); } catch { v = "?"; }
            return "{\"n\":" + Json.Str(name) + ",\"v\":" + Json.Str(v) + ",\"t\":" + Json.Str(QFormat.TypeName(value)) + "}";
        }

        static string Vars(object[] kv)
        {
            if (kv == null || kv.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Var((string)kv[i], kv[i + 1]));
            }
            return sb.Append(']').ToString();
        }

        // Regel wordt uitgevoerd.
        public static void L(int line)
        {
            if (!Enabled) return;
            Add("{\"k\":\"L\",\"l\":" + line + ",\"d\":" + _depth + "}");
        }

        // Waarden na het uitvoeren van een regel.
        public static void V(int line, params object[] kv)
        {
            if (!Enabled || kv == null || kv.Length == 0) return;
            Add("{\"k\":\"V\",\"l\":" + line + ",\"d\":" + _depth + ",\"vars\":" + Vars(kv) + "}");
        }

        // Evaluatie van een voorwaarde (if, while, for).
        public static bool C(int line, bool result, params object[] kv)
        {
            if (Enabled)
                Add("{\"k\":\"C\",\"l\":" + line + ",\"r\":" + (result ? "true" : "false") + ",\"d\":" + _depth + ",\"vars\":" + Vars(kv) + "}");
            return result;
        }

        // Activatie van een method.
        public static void Enter(string method, string file, int line, params object[] kv)
        {
            if (!Enabled) return;
            _depth++;
            Add("{\"k\":\"E\",\"m\":" + Json.Str(method) + ",\"f\":" + Json.Str(file) + ",\"l\":" + line + ",\"d\":" + _depth + ",\"vars\":" + Vars(kv) + "}");
        }

        public static void Exit()
        {
            if (!Enabled) return;
            Add("{\"k\":\"X\",\"d\":" + _depth + "}");
            if (_depth > 0) _depth--;
        }

        public static T Ret<T>(int line, T value)
        {
            if (Enabled)
            {
                string v;
                try { v = QFormat.Value(value, 0); } catch { v = "?"; }
                Add("{\"k\":\"R\",\"l\":" + line + ",\"d\":" + _depth + ",\"v\":" + Json.Str(v) + "}");
            }
            return value;
        }
    }
}
