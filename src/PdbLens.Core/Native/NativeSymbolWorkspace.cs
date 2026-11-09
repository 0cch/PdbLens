using System.Runtime.InteropServices;
using System.Text.Json;
using PdbLens.Core.Models;

namespace PdbLens.Core.Native;

internal static class NativeMethods
{
    private const string LibraryName = "PdbLens.Native.dll";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint PdbLens_CreateWorkspace();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PdbLens_DestroyWorkspace(nint workspace);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int PdbLens_OpenPdb(nint workspace, string path, out nint resultJson);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int PdbLens_ClosePdb(nint workspace, uint pdbId, out nint resultJson);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int PdbLens_QuerySymbols(
        nint workspace,
        uint pdbFilter,
        uint categories,
        string? searchText,
        uint offset,
        uint limit,
        out nint resultJson);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int PdbLens_LocateSymbol(
        nint workspace,
        uint pdbFilter,
        uint pdbId,
        uint symIndexId,
        uint categories,
        out nint resultJson);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int PdbLens_GetDefinition(
        nint workspace,
        uint pdbId,
        uint symIndexId,
        out nint resultJson);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void PdbLens_FreeString(nint value);
}

internal sealed class NativeSymbolWorkspace : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private nint _handle;

    private NativeSymbolWorkspace(nint handle)
    {
        _handle = handle;
    }

    public static NativeSymbolWorkspace Create()
    {
        var handle = NativeMethods.PdbLens_CreateWorkspace();
        if (handle == 0)
        {
            throw new InvalidOperationException("Failed to create the native PDB workspace.");
        }
        return new NativeSymbolWorkspace(handle);
    }

    public PdbFileInfo OpenPdb(string path)
    {
        var hr = NativeMethods.PdbLens_OpenPdb(_handle, path, out var json);
        return ReadResult<PdbFileInfo>(hr, json);
    }

    public void ClosePdb(uint pdbId)
    {
        var hr = NativeMethods.PdbLens_ClosePdb(_handle, pdbId, out var json);
        ReadResult<EmptyResult>(hr, json);
    }

    public SymbolPage QuerySymbols(
        uint pdbFilter,
        uint categories,
        string? searchText,
        uint offset,
        uint limit)
    {
        var hr = NativeMethods.PdbLens_QuerySymbols(
            _handle, pdbFilter, categories, searchText, offset, limit, out var json);
        return ReadResult<SymbolPage>(hr, json);
    }

    public LocatedSymbol LocateSymbol(
        uint pdbFilter,
        uint pdbId,
        uint symIndexId,
        uint categories)
    {
        var hr = NativeMethods.PdbLens_LocateSymbol(
            _handle, pdbFilter, pdbId, symIndexId, categories, out var json);
        return ReadResult<LocatedSymbol>(hr, json);
    }

    public SymbolDefinition GetDefinition(uint pdbId, uint symIndexId)
    {
        var hr = NativeMethods.PdbLens_GetDefinition(_handle, pdbId, symIndexId, out var json);
        return ReadResult<SymbolDefinition>(hr, json);
    }

    private static T ReadResult<T>(int hr, nint jsonPointer)
    {
        try
        {
            var json = jsonPointer == 0
                ? null
                : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(jsonPointer);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException($"Native call failed with HRESULT 0x{hr:X8}.");
            }

            if (hr < 0)
            {
                var error = JsonSerializer.Deserialize<NativeError>(json, JsonOptions);
                throw new InvalidOperationException(
                    error?.Error ?? $"Native call failed with HRESULT 0x{hr:X8}.");
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException("Native call returned an empty response.");
        }
        finally
        {
            if (jsonPointer != 0)
            {
                NativeMethods.PdbLens_FreeString(jsonPointer);
            }
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            NativeMethods.PdbLens_DestroyWorkspace(_handle);
            _handle = 0;
        }
    }

    private sealed record NativeError(bool Ok, int Hr, string Error);
    private sealed record EmptyResult(bool Ok);
}
