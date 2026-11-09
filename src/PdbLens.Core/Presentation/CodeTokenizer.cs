namespace PdbLens.Core.Presentation;

public enum CodeTokenKind
{
    Plain,
    Keyword,
    Type,
    Number,
    Comment,
    String
}

public readonly record struct CodeToken(int Start, int Length, CodeTokenKind Kind);

public static class CodeTokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "auto", "bool", "break", "case", "catch", "char",
        "class", "const", "constexpr", "continue", "default", "delete", "do",
        "double", "else", "enum", "explicit", "extern", "false", "float", "for",
        "friend", "if", "inline", "int", "long", "mutable", "namespace", "new",
        "noexcept", "nullptr", "operator", "private", "protected", "public",
        "register", "return", "short", "signed", "sizeof", "static", "struct",
        "switch", "template", "this", "throw", "true", "try", "typedef", "typename",
        "union", "unsigned", "using", "virtual", "void", "volatile", "while",
        "__cdecl", "__fastcall", "__stdcall", "__thiscall", "__vectorcall"
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "BOOLEAN", "BSTR", "BYTE", "CHAR", "DWORD", "DWORD64", "FLOAT", "GUID",
        "HRESULT", "INT", "INT64", "LONG", "LONGLONG", "LPSTR", "LPCSTR",
        "LPCWSTR", "LPWSTR", "PVOID", "QWORD", "UCHAR", "UINT", "UINT64",
        "ULONG", "ULONGLONG", "UNICODE_STRING", "USHORT", "VARIANT", "VOID",
        "WCHAR", "WORD"
    };

    public static IReadOnlyList<CodeToken> Tokenize(string code)
    {
        var tokens = new List<CodeToken>();
        var index = 0;
        while (index < code.Length)
        {
            var start = index;
            CodeTokenKind kind;

            if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '/')
            {
                var end = code.IndexOf('\n', index);
                index = end < 0 ? code.Length : end;
                kind = CodeTokenKind.Comment;
            }
            else if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '*')
            {
                var close = code.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? code.Length : close + 2;
                kind = CodeTokenKind.Comment;
            }
            else if (code[index] is '"' or '\'')
            {
                var quote = code[index++];
                while (index < code.Length)
                {
                    if (code[index] == '\\' && index + 1 < code.Length)
                    {
                        index += 2;
                        continue;
                    }
                    if (code[index++] == quote)
                    {
                        break;
                    }
                }
                kind = CodeTokenKind.String;
            }
            else if (char.IsDigit(code[index]))
            {
                index++;
                while (index < code.Length &&
                       (char.IsLetterOrDigit(code[index]) || code[index] is 'x' or 'X' or '.' or '_'))
                {
                    index++;
                }
                kind = CodeTokenKind.Number;
            }
            else if (char.IsLetter(code[index]) || code[index] == '_')
            {
                index++;
                while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] == '_'))
                {
                    index++;
                }
                var text = code.Substring(start, index - start);
                kind = Types.Contains(text)
                    ? CodeTokenKind.Type
                    : Keywords.Contains(text)
                        ? CodeTokenKind.Keyword
                        : CodeTokenKind.Plain;
            }
            else
            {
                index++;
                kind = CodeTokenKind.Plain;
            }

            if (tokens.Count > 0 && tokens[^1].Kind == kind &&
                tokens[^1].Start + tokens[^1].Length == start)
            {
                var previous = tokens[^1];
                tokens[^1] = previous with { Length = previous.Length + index - start };
            }
            else
            {
                tokens.Add(new CodeToken(start, index - start, kind));
            }
        }

        return tokens;
    }
}
