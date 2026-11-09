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
    string DisplayKind,
    string PdbName)
{
    public string Key => $"{PdbId}:{SymIndexId}";
    public string Subtitle => $"{DisplayKind} · {PdbName}";
    public string Glyph => Kind switch
    {
        "struct" or "class" or "union" => "\uE8F1",
        "enum" => "\uE8EF",
        "typedef" => "\uE8C8",
        "function" => "\uE8A1",
        "data" => "\uE8B7",
        "publicSymbol" => "\uE8D7",
        _ => "\uE8B7"
    };
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
    public string Subtitle => $"{DisplayKind} · PDB {PdbId}";
    public string Glyph => Kind switch
    {
        "struct" or "class" or "union" => "\uE8F1",
        "enum" => "\uE8EF",
        "typedef" => "\uE8C8",
        "function" => "\uE8A1",
        "data" => "\uE8B7",
        "publicSymbol" => "\uE8D7",
        _ => "\uE8B7"
    };
}

public sealed record LocatedSymbol(
    bool Ok,
    uint PdbId,
    uint SymIndexId,
    int Ordinal);
