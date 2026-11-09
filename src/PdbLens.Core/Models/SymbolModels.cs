namespace PdbLens.Core.Models;

public sealed record PdbFileInfo(
    uint PdbId,
    string Path,
    string Name,
    long SymbolCount)
{
    public string DisplayName => $"{Name} ({SymbolCount:N0})";
}

public sealed record SymbolItem(
    uint PdbId,
    uint SymIndexId,
    string Name,
    string Kind,
    string DisplayKind)
{
    public string Key => $"{PdbId}:{SymIndexId}";
}

public sealed record SymbolPage(
    bool Ok,
    int Offset,
    int Limit,
    int Total,
    bool HasMore,
    IReadOnlyList<SymbolItem> Items);

public sealed record SymbolLink(
    int Start,
    int Length,
    uint TargetPdbId,
    uint TargetSymIndexId,
    string TargetName,
    string TargetKind);

public sealed record SymbolDefinition(
    bool Ok,
    uint PdbId,
    uint SymIndexId,
    string Name,
    string Kind,
    string DisplayKind,
    string Code,
    IReadOnlyList<SymbolLink> Links)
{
    public string Key => $"{PdbId}:{SymIndexId}";
}

public sealed record LocatedSymbol(
    bool Ok,
    uint PdbId,
    uint SymIndexId,
    int Ordinal);
