using System.Net;
using System.Text;
using PdbLens.Core.Models;

namespace PdbLens.Core.Presentation;

public static class CodeHtmlRenderer
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

    public static string RenderDocument(string code, IReadOnlyList<SymbolLink>? links)
    {
        var body = new StringBuilder(code.Length * 2);
        var linksByStart = (links ?? Array.Empty<SymbolLink>())
            .Where(link => link.Start >= 0 && link.Length > 0 && link.Start + link.Length <= code.Length)
            .OrderBy(link => link.Start)
            .GroupBy(link => link.Start)
            .ToDictionary(group => group.Key, group => group.First());

        var classes = Classify(code);
        var lineStart = 0;
        var lineNumber = 1;
        while (lineStart <= code.Length)
        {
            var newline = code.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? code.Length : newline;
            var contentEnd = lineEnd;
            if (contentEnd > lineStart && code[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }

            body.Append("<div class=\"line\"><span class=\"ln\">");
            body.Append(lineNumber);
            body.Append("</span><span class=\"src\">");
            RenderLine(body, code, classes, linksByStart, lineStart, contentEnd);
            body.Append("</span></div>");

            if (newline < 0)
            {
                break;
            }
            lineStart = newline + 1;
            lineNumber++;
        }

        return """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
:root { color-scheme: dark; }
html, body { margin: 0; padding: 0; background: #11151b; color: #d9e0ea; }
body { font-family: "Cascadia Code", "Consolas", monospace; font-size: 13px; }
.code { min-height: 100vh; padding: 12px 0 40px; }
.line { display: flex; min-height: 20px; line-height: 20px; }
.line:hover { background: #18202a; }
.ln { width: 58px; flex: 0 0 58px; padding-right: 14px; text-align: right; color: #566170; user-select: none; }
.src { white-space: pre; padding-right: 24px; }
.kw { color: #7aa2f7; }
.typ { color: #7dcfff; }
.num { color: #ff9e64; }
.com { color: #6f7f8f; font-style: italic; }
.str { color: #9ece6a; }
a.sym { color: #9ece6a; text-decoration: none; border-bottom: 1px dotted #9ece6a; cursor: pointer; }
a.sym:hover { color: #b9f27c; border-bottom-color: #b9f27c; }
</style>
</head>
<body>
<div class="code">
""" + body + """
</div>
<script>
document.addEventListener('click', function (event) {
    const anchor = event.target.closest('a.sym');
    if (!anchor) return;
    event.preventDefault();
    window.chrome.webview.postMessage({
        type: 'symbol',
        pdbId: Number(anchor.dataset.pdb),
        symIndexId: Number(anchor.dataset.id),
        kind: anchor.dataset.kind || ''
    });
});
</script>
</body>
</html>
""";
    }

    private static void RenderLine(
        StringBuilder output,
        string code,
        byte[] classes,
        IReadOnlyDictionary<int, SymbolLink> linksByStart,
        int start,
        int end)
    {
        if (start == end)
        {
            output.Append("&nbsp;");
            return;
        }

        var index = start;
        while (index < end)
        {
            if (linksByStart.TryGetValue(index, out var link))
            {
                var length = Math.Min(link.Length, end - index);
                if (length > 0)
                {
                    output.Append("<a class=\"sym\" data-pdb=\"");
                    output.Append(link.TargetPdbId);
                    output.Append("\" data-id=\"");
                    output.Append(link.TargetSymIndexId);
                    output.Append("\" data-kind=\"");
                    output.Append(WebUtility.HtmlEncode(link.TargetKind));
                    output.Append("\" title=\"");
                    output.Append(WebUtility.HtmlEncode(link.TargetName));
                    output.Append("\">");
                    output.Append(WebUtility.HtmlEncode(code.Substring(index, length)));
                    output.Append("</a>");
                    index += length;
                    continue;
                }
            }

            var kind = classes[index];
            var segmentEnd = index + 1;
            while (segmentEnd < end &&
                   classes[segmentEnd] == kind &&
                   !linksByStart.ContainsKey(segmentEnd))
            {
                segmentEnd++;
            }

            var cssClass = kind switch
            {
                1 => "kw",
                2 => "typ",
                3 => "num",
                4 => "com",
                5 => "str",
                _ => null
            };
            var text = WebUtility.HtmlEncode(code.Substring(index, segmentEnd - index));
            if (cssClass is null)
            {
                output.Append(text);
            }
            else
            {
                output.Append("<span class=\"");
                output.Append(cssClass);
                output.Append("\">");
                output.Append(text);
                output.Append("</span>");
            }
            index = segmentEnd;
        }
    }

    private static byte[] Classify(string code)
    {
        var classes = new byte[code.Length];
        var index = 0;
        while (index < code.Length)
        {
            if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '/')
            {
                var end = code.IndexOf('\n', index);
                if (end < 0) end = code.Length;
                Fill(classes, index, end, 4);
                index = end;
                continue;
            }

            if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '*')
            {
                var close = code.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = close < 0 ? code.Length : close + 2;
                Fill(classes, index, end, 4);
                index = end;
                continue;
            }

            if (code[index] is '"' or '\'')
            {
                var quote = code[index];
                var end = index + 1;
                while (end < code.Length)
                {
                    if (code[end] == '\\' && end + 1 < code.Length)
                    {
                        end += 2;
                        continue;
                    }
                    if (code[end] == quote)
                    {
                        end++;
                        break;
                    }
                    end++;
                }
                Fill(classes, index, end, 5);
                index = end;
                continue;
            }

            if (char.IsDigit(code[index]))
            {
                var end = index + 1;
                while (end < code.Length &&
                       (char.IsLetterOrDigit(code[end]) || code[end] is 'x' or 'X' or '.' or '_'))
                {
                    end++;
                }
                Fill(classes, index, end, 3);
                index = end;
                continue;
            }

            if (char.IsLetter(code[index]) || code[index] == '_')
            {
                var end = index + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
                {
                    end++;
                }

                var token = code.Substring(index, end - index);
                var kind = Types.Contains(token) ? (byte)2 : Keywords.Contains(token) ? (byte)1 : (byte)0;
                Fill(classes, index, end, kind);
                index = end;
                continue;
            }

            index++;
        }
        return classes;
    }

    private static void Fill(byte[] classes, int start, int end, byte value)
    {
        for (var index = start; index < end; index++)
        {
            classes[index] = value;
        }
    }
}
