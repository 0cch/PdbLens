namespace PdbLens.App;

public sealed record PdbManagerItem(
    uint PdbId,
    string Name,
    string Path,
    long? SymbolCount)
{
    public bool IsPending => PdbId == 0;

    public string Detail => System.IO.Path.GetDirectoryName(Path) ?? Path;

    public string SymbolText => SymbolCount.HasValue
        ? $"{SymbolCount.Value:N0} symbols"
        : "Pending";
}
