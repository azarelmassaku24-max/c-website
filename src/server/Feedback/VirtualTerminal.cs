using System.Text;

namespace CSharpQuest.Server.Feedback;

/// <summary>
/// Een eenvoudige terminal-emulator: speelt de ANSI-uitvoer van een programma af op een virtueel
/// scherm. Daarmee kunnen we programma's met Console.Clear of SetCursorPosition beoordelen op wat
/// de gebruiker uiteindelijk ziet.
/// </summary>
public sealed class VirtualTerminal
{
    readonly List<StringBuilder> _lines = new() { new StringBuilder() };
    int _row, _col;
    readonly int _width;

    public VirtualTerminal(int width = 100) { _width = width; }

    public static string Render(string output, int width = 100)
    {
        var vt = new VirtualTerminal(width);
        vt.Feed(output);
        return vt.Screen();
    }

    public void Feed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\u001b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                int j = i + 2;
                var param = new StringBuilder();
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';' || s[j] == '?')) param.Append(s[j++]);
                if (j >= s.Length) break;
                char cmd = s[j];
                Csi(cmd, param.ToString());
                i = j;
                continue;
            }
            switch (c)
            {
                case '\n': _row++; _col = 0; Ensure(); break;
                case '\r': _col = 0; break;
                case '\b': if (_col > 0) _col--; break;
                case '\a': break;
                case '\f': break;
                default:
                    Put(c);
                    break;
            }
        }
    }

    void Csi(char cmd, string param)
    {
        var parts = param.Replace("?", "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        switch (cmd)
        {
            case 'H':
            case 'f':
                _row = Math.Max(0, (parts.Length > 0 ? parts[0] : 1) - 1);
                _col = Math.Max(0, (parts.Length > 1 ? parts[1] : 1) - 1);
                Ensure();
                break;
            case 'J':
                if (parts.Length > 0 && (parts[0] == 2 || parts[0] == 3)) { _lines.Clear(); _lines.Add(new StringBuilder()); _row = 0; _col = 0; }
                break;
            case 'K':
                Ensure();
                if (_lines[_row].Length > _col) _lines[_row].Length = _col;
                break;
            case 'A': _row = Math.Max(0, _row - (parts.Length > 0 ? parts[0] : 1)); break;
            case 'B': _row += parts.Length > 0 ? parts[0] : 1; Ensure(); break;
            case 'C': _col += parts.Length > 0 ? parts[0] : 1; break;
            case 'D': _col = Math.Max(0, _col - (parts.Length > 0 ? parts[0] : 1)); break;
            default: break; // kleuren (m), cursor zichtbaar (h/l), ...
        }
    }

    void Ensure()
    {
        while (_lines.Count <= _row) _lines.Add(new StringBuilder());
    }

    void Put(char c)
    {
        Ensure();
        var line = _lines[_row];
        while (line.Length < _col) line.Append(' ');
        if (line.Length == _col) line.Append(c); else line[_col] = c;
        _col++;
        if (_col >= _width) { _col = 0; _row++; Ensure(); }
    }

    public string Screen()
    {
        var text = string.Join("\n", _lines.Select(l => l.ToString().TrimEnd()));
        return text.TrimEnd('\n');
    }

    /// <summary>Verwijdert alle ANSI-codes en vervangt Console.Clear door een lege regel-markering.</summary>
    public static string StripAnsi(string s, bool markClears = false)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\u001b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                int j = i + 2;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';' || s[j] == '?')) j++;
                if (j < s.Length && s[j] == 'J' && markClears && (sb.Length == 0 || !sb.ToString().EndsWith("[scherm gewist]\n")))
                {
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                    sb.Append("[scherm gewist]\n");
                }
                i = j;
                continue;
            }
            if (s[i] == '\a') continue;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
