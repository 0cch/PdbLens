using PdbLens.Core.Models;
using PdbLens.Core.Presentation;
using PdbLens.Core.Services;

namespace PdbLens.Core.Tests;

public sealed class CodeHtmlRendererTests
{
    [Fact]
    public void RenderDocument_ProducesClickableSymbolAnchor()
    {
        const string code = "struct Demo {\r\n\tChild Value;\r\n};";
        var childStart = code.IndexOf("Child", StringComparison.Ordinal);
        var link = new SymbolLink(
            childStart,
            "Child".Length,
            7,
            42,
            "Child",
            "struct");

        var html = CodeHtmlRenderer.RenderDocument(code, new[] { link });

        Assert.Contains("data-pdb=\"7\"", html, StringComparison.Ordinal);
        Assert.Contains("data-id=\"42\"", html, StringComparison.Ordinal);
        Assert.Contains(">Child</a>", html, StringComparison.Ordinal);
        Assert.Contains("struct", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDocument_EncodesCodeMarkup()
    {
        var html = CodeHtmlRenderer.RenderDocument("struct Demo { int *Value; };", Array.Empty<SymbolLink>());
        Assert.Contains("class=\"kw\">struct</span>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"kw\">int</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryFlags_MapKinds()
    {
        Assert.Equal(SymbolCategoryFlags.Struct, SymbolCategoryFlags.FromKind("struct"));
        Assert.Equal(SymbolCategoryFlags.Function, SymbolCategoryFlags.FromKind("function"));
        Assert.Equal(0u, SymbolCategoryFlags.FromKind("unknown"));
    }
}
