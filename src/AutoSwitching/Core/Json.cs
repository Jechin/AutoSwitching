using System.Globalization;
using System.Text;

namespace AutoSwitching.Core;

/// <summary>
/// 极简 JSON 写入器（带缩进）。
/// </summary>
/// <remarks>
/// 之所以不用 System.Text.Json：它在 .NET Framework 4.8 上需要额外分发
/// System.Text.Json.dll、System.Memory.dll、System.Buffers.dll 等约 1.5 MB 的程序集，
/// 与本项目「单文件、零依赖」的目标冲突。
/// 这里只实现配置文件需要的子集：对象、数组、字符串、数字、布尔、null。
/// </remarks>
internal sealed class JsonWriter
{
    private readonly StringBuilder _sb = new();
    private readonly List<bool> _hasItems = new();
    private int _depth;
    private bool _afterName;

    public override string ToString() => _sb.ToString();

    public void BeginObject()
    {
        BeforeItem();
        _sb.Append('{');
        Push();
    }

    public void EndObject() => End('}');

    public void BeginArray()
    {
        BeforeItem();
        _sb.Append('[');
        Push();
    }

    public void EndArray() => End(']');

    /// <summary>写属性名，后面必须紧跟一个值。</summary>
    public void Name(string name)
    {
        BeforeItem();
        WriteString(name);
        _sb.Append(": ");
        _afterName = true;
    }

    public void Value(string? value)
    {
        BeforeItem();
        if (value is null)
        {
            _sb.Append("null");
        }
        else
        {
            WriteString(value);
        }
    }

    public void Value(bool value)
    {
        BeforeItem();
        _sb.Append(value ? "true" : "false");
    }

    public void Value(int value)
    {
        BeforeItem();
        _sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public void Null()
    {
        BeforeItem();
        _sb.Append("null");
    }

    private void Push()
    {
        _depth++;
        _hasItems.Add(false);
    }

    private void End(char close)
    {
        _depth--;
        bool hadItems = _hasItems[_depth];
        _hasItems.RemoveAt(_depth);

        if (hadItems)
        {
            NewLine();
        }

        _sb.Append(close);
    }

    private void BeforeItem()
    {
        // 属性名之后的值紧跟在同一行，不额外加逗号或换行。
        if (_afterName)
        {
            _afterName = false;
            return;
        }

        if (_depth == 0)
        {
            return;
        }

        if (_hasItems[_depth - 1])
        {
            _sb.Append(',');
        }

        _hasItems[_depth - 1] = true;
        NewLine();
    }

    private void NewLine()
    {
        _sb.Append(Environment.NewLine);
        _sb.Append(' ', _depth * 2);
    }

    private void WriteString(string value)
    {
        _sb.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\b': _sb.Append("\\b"); break;
                case '\f': _sb.Append("\\f"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    // 控制字符必须转义；中文等非 ASCII 字符原样输出，保持文件可读。
                    if (c < ' ')
                    {
                        _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _sb.Append(c);
                    }

                    break;
            }
        }

        _sb.Append('"');
    }
}

/// <summary>
/// 极简 JSON 解析器。
/// 对象解析为 <see cref="Dictionary{TKey,TValue}"/>（键不区分大小写），
/// 数组解析为 <see cref="List{T}"/>，数字解析为 <see cref="double"/>。
/// </summary>
internal static class JsonReader
{
    public static object? Parse(string text)
    {
        int index = 0;
        object? value = ParseValue(text, ref index);
        SkipWhitespace(text, ref index);

        if (index < text.Length)
        {
            throw new FormatException("JSON 末尾存在多余内容。");
        }

        return value;
    }

    private static object? ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);

        if (i >= s.Length)
        {
            throw new FormatException("JSON 意外结束。");
        }

        switch (s[i])
        {
            case '{':
                return ParseObject(s, ref i);
            case '[':
                return ParseArray(s, ref i);
            case '"':
                return ParseString(s, ref i);
            case 't':
                Expect(s, ref i, "true");
                return true;
            case 'f':
                Expect(s, ref i, "false");
                return false;
            case 'n':
                Expect(s, ref i, "null");
                return null;
            default:
                return ParseNumber(s, ref i);
        }
    }

    private static Dictionary<string, object?> ParseObject(string s, ref int i)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        i++; // 跳过 '{'
        SkipWhitespace(s, ref i);

        if (i < s.Length && s[i] == '}')
        {
            i++;
            return result;
        }

        while (true)
        {
            SkipWhitespace(s, ref i);

            if (i >= s.Length || s[i] != '"')
            {
                throw new FormatException("JSON 对象的属性名必须是字符串。");
            }

            string name = ParseString(s, ref i);
            SkipWhitespace(s, ref i);

            if (i >= s.Length || s[i] != ':')
            {
                throw new FormatException("JSON 对象的属性名后缺少冒号。");
            }

            i++;
            result[name] = ParseValue(s, ref i);
            SkipWhitespace(s, ref i);

            if (i >= s.Length)
            {
                throw new FormatException("JSON 对象未闭合。");
            }

            if (s[i] == ',')
            {
                i++;
                continue;
            }

            if (s[i] == '}')
            {
                i++;
                return result;
            }

            throw new FormatException("JSON 对象中出现意外字符。");
        }
    }

    private static List<object?> ParseArray(string s, ref int i)
    {
        var result = new List<object?>();
        i++; // 跳过 '['
        SkipWhitespace(s, ref i);

        if (i < s.Length && s[i] == ']')
        {
            i++;
            return result;
        }

        while (true)
        {
            result.Add(ParseValue(s, ref i));
            SkipWhitespace(s, ref i);

            if (i >= s.Length)
            {
                throw new FormatException("JSON 数组未闭合。");
            }

            if (s[i] == ',')
            {
                i++;
                continue;
            }

            if (s[i] == ']')
            {
                i++;
                return result;
            }

            throw new FormatException("JSON 数组中出现意外字符。");
        }
    }

    private static string ParseString(string s, ref int i)
    {
        i++; // 跳过起始引号
        var sb = new StringBuilder();

        while (i < s.Length)
        {
            char c = s[i++];

            if (c == '"')
            {
                return sb.ToString();
            }

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (i >= s.Length)
            {
                break;
            }

            char esc = s[i++];
            switch (esc)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length)
                    {
                        throw new FormatException("JSON 的 \\u 转义不完整。");
                    }

                    sb.Append((char)ushort.Parse(
                        s.Substring(i, 4),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    throw new FormatException("JSON 中存在无法识别的转义字符。");
            }
        }

        throw new FormatException("JSON 字符串未闭合。");
    }

    private static double ParseNumber(string s, ref int i)
    {
        int start = i;

        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
        {
            i++;
        }

        if (start == i)
        {
            throw new FormatException("JSON 中出现无法识别的值。");
        }

        return double.Parse(
            s.Substring(start, i - start),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    private static void Expect(string s, ref int i, string literal)
    {
        if (i + literal.Length > s.Length ||
            string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
        {
            throw new FormatException($"JSON 中期望 \"{literal}\"。");
        }

        i += literal.Length;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }
    }
}
