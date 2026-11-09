using PdbLens.Core.Services;

namespace PdbLens.Core.Tests;

public sealed class SymbolWorkspaceServiceTests
{
    [Fact]
    public async Task Workspace_QueriesPrivateUdtAndProducesLinks()
    {
        var pdbPath = Path.Combine(AppContext.BaseDirectory, "PdbLens.Native.Tests.pdb");
        Assert.True(File.Exists(pdbPath), $"Test PDB not found: {pdbPath}");

        await using var service = new SymbolWorkspaceService();
        var pdb = await service.OpenPdbAsync(pdbPath);
        var page = await service.QuerySymbolsAsync(
            0,
            SymbolCategoryFlags.Default,
            "PdbLensTestStruct",
            0,
            20);

        var symbol = Assert.Single(page.Items);
        Assert.Equal("PdbLensTestStruct", symbol.Name);

        var definition = await service.GetDefinitionAsync(symbol.PdbId, symbol.SymIndexId);
        Assert.Contains("PdbLensTestChild", definition.Code, StringComparison.Ordinal);
        Assert.NotEmpty(definition.Links);
        Assert.Contains(definition.Links, link => link.TargetName == "PdbLensTestChild");
        Assert.True(pdb.PdbId > 0);
    }

    [Fact]
    public async Task Workspace_LoadsMultiplePdbsAndFiltersByPdb()
    {
        var pdbPath = Path.Combine(AppContext.BaseDirectory, "PdbLens.Native.Tests.pdb");
        await using var service = new SymbolWorkspaceService();

        var first = await service.OpenPdbAsync(pdbPath);
        var second = await service.OpenPdbAsync(pdbPath);

        var firstPage = await service.QuerySymbolsAsync(
            first.PdbId, SymbolCategoryFlags.Default, "PdbLensTestStruct", 0, 20);
        var secondPage = await service.QuerySymbolsAsync(
            second.PdbId, SymbolCategoryFlags.Default, "PdbLensTestStruct", 0, 20);
        var allPage = await service.QuerySymbolsAsync(
            0, SymbolCategoryFlags.Default, "PdbLensTestStruct", 0, 20);

        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.Equal(2, allPage.Total);

        await service.ClosePdbAsync(first.PdbId);
        var remaining = await service.QuerySymbolsAsync(
            0, SymbolCategoryFlags.Default, "PdbLensTestStruct", 0, 20);
        Assert.Single(remaining.Items);
        Assert.Equal(second.PdbId, remaining.Items[0].PdbId);
    }
}
