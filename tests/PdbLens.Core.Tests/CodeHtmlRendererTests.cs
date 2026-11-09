using PdbLens.Core.Presentation;
using PdbLens.Core.Services;

namespace PdbLens.Core.Tests;

public sealed class CodeTokenizerTests
{
    [Fact]
    public void Tokenize_ClassifiesKeywordsTypesNumbersAndComments()
    {
        const string code = "struct Demo { LONG Value; // count\n int x = 42; };";
        var tokens = CodeTokenizer.Tokenize(code);

        Assert.Contains(tokens, token => token.Kind == CodeTokenKind.Keyword && Slice(code, token) == "struct");
        Assert.Contains(tokens, token => token.Kind == CodeTokenKind.Type && Slice(code, token) == "LONG");
        Assert.Contains(tokens, token => token.Kind == CodeTokenKind.Number && Slice(code, token) == "42");
        Assert.Contains(tokens, token => token.Kind == CodeTokenKind.Comment && Slice(code, token).StartsWith("//", StringComparison.Ordinal));
    }

    [Fact]
    public void Tokenize_LeavesIdentifiersAsPlainText()
    {
        const string code = "struct Demo { LONG Value; };";
        var tokens = CodeTokenizer.Tokenize(code);

        Assert.Contains(tokens, token => token.Kind == CodeTokenKind.Plain && Slice(code, token).Contains("Demo", StringComparison.Ordinal));
    }

    [Fact]
    public void CategoryFlags_MapKinds()
    {
        Assert.Equal(SymbolCategoryFlags.Struct, SymbolCategoryFlags.FromKind("struct"));
        Assert.Equal(SymbolCategoryFlags.Function, SymbolCategoryFlags.FromKind("function"));
        Assert.Equal(0u, SymbolCategoryFlags.FromKind("unknown"));
    }

    private static string Slice(string source, CodeToken token) => source.Substring(token.Start, token.Length);
}
