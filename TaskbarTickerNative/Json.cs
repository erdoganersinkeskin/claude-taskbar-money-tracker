using System.Globalization;
using System.Text;

namespace TaskbarTickerNative;

/// Küçük bir JSON okuyucu/yazıcı. System.Text.Json yerel derlemede exe'yi belirgin biçimde büyüttüğü için
/// yalnızca gereken kadarı burada yazıldı. Yorumları (// ve /* */) ve sondaki virgülleri hoş görür.
/// Değerler: Dictionary&lt;string, object?&gt; (anahtarlar büyük/küçük harf duyarsız), List&lt;object?&gt;, string, double, bool, null.
internal static class Json
{
    public static object? Parse(string text)
    {
        int i = 0;
        object? value = Value(text, ref i);
        return value;
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == '﻿') i++;
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
            }
            else break;
        }
    }

    private static object? Value(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length) throw new FormatException("JSON: beklenmeyen son");
        char c = s[i];
        switch (c)
        {
            case '{':
            {
                i++;
                var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return obj; }
                    string key = String(s, ref i);
                    Skip(s, ref i);
                    Expect(s, ref i, ':');
                    obj[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') i++;
                }
            }
            case '[':
            {
                i++;
                var list = new List<object?>();
                while (true)
                {
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return list; }
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') i++;
                }
            }
            case '"':
                return String(s, ref i);
            case 't' when string.CompareOrdinal(s, i, "true", 0, 4) == 0:
                i += 4;
                return true;
            case 'f' when string.CompareOrdinal(s, i, "false", 0, 5) == 0:
                i += 5;
                return false;
            case 'n' when string.CompareOrdinal(s, i, "null", 0, 4) == 0:
                i += 4;
                return null;
            default:
            {
                int start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] is '-' or '+' or '.' or 'e' or 'E')) i++;
                if (i == start) throw new FormatException("JSON: beklenmeyen karakter");
                return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    private static void Expect(string s, ref int i, char c)
    {
        if (i >= s.Length || s[i] != c) throw new FormatException("JSON: '" + c + "' bekleniyordu");
        i++;
    }

    private static string String(string s, ref int i)
    {
        Expect(s, ref i, '"');
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= s.Length) throw new FormatException("JSON: kapanmamış metin");
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i >= s.Length) throw new FormatException("JSON: kapanmamış metin");
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break; // \" \\ \/
            }
        }
    }

    // ---- okuma yardımcıları

    public static Dictionary<string, object?>? Obj(object? v) => v as Dictionary<string, object?>;
    public static List<object?>? Arr(object? v) => v as List<object?>;

    public static object? Get(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) ? v : null;
    public static string Str(Dictionary<string, object?> o, string key, string fallback) => Get(o, key) as string ?? fallback;
    public static double Num(Dictionary<string, object?> o, string key, double fallback) => Get(o, key) is double d ? d : fallback;
    public static bool Bool(Dictionary<string, object?> o, string key, bool fallback) => Get(o, key) is bool b ? b : fallback;

    // ---- yazma yardımcıları

    public static string Quote(string? s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
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
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    public static string Number(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
