using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PdbLens.Core.Models;
using PdbLens.Core.Presentation;
using Windows.UI;
using Windows.UI.Text;

namespace PdbLens.App;

internal static class CodeDocumentRenderer
{
    public static void Render(
        RichTextBlock target,
        TextBlock lineNumbers,
        string code,
        IReadOnlyList<SymbolLink> links,
        bool darkTheme,
        Func<SymbolLink, Task> navigateAsync)
    {
        target.Blocks.Clear();

        var kinds = new CodeTokenKind[code.Length];
        foreach (var token in CodeTokenizer.Tokenize(code))
        {
            for (var index = token.Start; index < token.Start + token.Length; index++)
            {
                kinds[index] = token.Kind;
            }
        }

        var linksByStart = links
            .Where(link => link.Start >= 0 && link.Length > 0 && link.Start + link.Length <= code.Length)
            .OrderBy(link => link.Start)
            .GroupBy(link => link.Start)
            .ToDictionary(group => group.Key, group => group.First());

        var palette = Palette.Create(darkTheme);
        var lineNumberValues = new List<string>();
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

            lineNumberValues.Add(lineNumber.ToString());
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            RenderLine(paragraph, code, kinds, linksByStart, palette, lineStart, contentEnd, navigateAsync);
            target.Blocks.Add(paragraph);

            if (newline < 0)
            {
                break;
            }
            lineStart = newline + 1;
            lineNumber++;
        }

        lineNumbers.Text = string.Join('\n', lineNumberValues);
    }

    private static void RenderLine(
        Paragraph paragraph,
        string code,
        CodeTokenKind[] kinds,
        IReadOnlyDictionary<int, SymbolLink> linksByStart,
        Palette palette,
        int start,
        int end,
        Func<SymbolLink, Task> navigateAsync)
    {
        var index = start;
        while (index < end)
        {
            if (linksByStart.TryGetValue(index, out var link))
            {
                var length = Math.Min(link.Length, end - index);
                if (length > 0)
                {
                    var hyperlink = new Hyperlink { Foreground = palette.Link };
                    hyperlink.Inlines.Add(new Run { Text = NormalizeText(code.Substring(index, length)) });
                    hyperlink.Click += async (_, _) => await navigateAsync(link);
                    paragraph.Inlines.Add(hyperlink);
                    index += length;
                    continue;
                }
            }

            var kind = kinds[index];
            var segmentEnd = index + 1;
            while (segmentEnd < end &&
                   kinds[segmentEnd] == kind &&
                   !linksByStart.ContainsKey(segmentEnd))
            {
                segmentEnd++;
            }

            var run = new Run { Text = NormalizeText(code.Substring(index, segmentEnd - index)) };
            switch (kind)
            {
                case CodeTokenKind.Keyword:
                    run.Foreground = palette.Keyword;
                    break;
                case CodeTokenKind.Type:
                    run.Foreground = palette.Type;
                    break;
                case CodeTokenKind.Number:
                    run.Foreground = palette.Number;
                    break;
                case CodeTokenKind.Comment:
                    run.Foreground = palette.Comment;
                    run.FontStyle = FontStyle.Italic;
                    break;
                case CodeTokenKind.String:
                    run.Foreground = palette.String;
                    break;
            }

            paragraph.Inlines.Add(run);
            index = segmentEnd;
        }
    }

    private static string NormalizeText(string value) => value.Replace("\t", "    ", StringComparison.Ordinal);

    private sealed class Palette
    {
        public required SolidColorBrush Keyword { get; init; }
        public required SolidColorBrush Type { get; init; }
        public required SolidColorBrush Number { get; init; }
        public required SolidColorBrush Comment { get; init; }
        public required SolidColorBrush String { get; init; }
        public required SolidColorBrush Link { get; init; }

        public static Palette Create(bool dark)
        {
            return dark
                ? new Palette
                {
                    Keyword = Brush(108, 182, 255),
                    Type = Brush(76, 194, 255),
                    Number = Brush(240, 163, 94),
                    Comment = Brush(139, 148, 158),
                    String = Brush(123, 216, 143),
                    Link = Brush(114, 213, 255)
                }
                : new Palette
                {
                    Keyword = Brush(0, 55, 166),
                    Type = Brush(0, 103, 192),
                    Number = Brush(142, 86, 46),
                    Comment = Brush(106, 115, 125),
                    String = Brush(15, 123, 15),
                    Link = Brush(0, 103, 192)
                };
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
            new(Color.FromArgb(255, red, green, blue));
    }
}
