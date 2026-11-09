using System.Collections.Concurrent;
using PdbLens.Core.Models;
using PdbLens.Core.Native;

namespace PdbLens.Core.Services;

public sealed class SymbolCategoryFlags
{
    public const uint Struct = 1u << 0;
    public const uint Class = 1u << 1;
    public const uint Union = 1u << 2;
    public const uint Enum = 1u << 3;
    public const uint Typedef = 1u << 4;
    public const uint Function = 1u << 5;
    public const uint Data = 1u << 6;
    public const uint PublicSymbol = 1u << 7;

    public const uint Default = Struct | Class | Union | Enum | Typedef | Function;

    public static uint FromKind(string kind) => kind switch
    {
        "struct" => Struct,
        "class" => Class,
        "union" => Union,
        "enum" => Enum,
        "typedef" => Typedef,
        "function" => Function,
        "data" => Data,
        "publicSymbol" => PublicSymbol,
        _ => 0
    };
}

public sealed class SymbolWorkspaceService : IAsyncDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeSymbolWorkspace? _workspace;
    private Exception? _startupError;
    private bool _disposed;

    public SymbolWorkspaceService()
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "PdbLens.DIA"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
        if (_startupError is not null)
        {
            throw new InvalidOperationException("Failed to initialize the native workspace.", _startupError);
        }
    }

    public Task<PdbFileInfo> OpenPdbAsync(string path, CancellationToken cancellationToken = default) =>
        Enqueue(workspace => workspace.OpenPdb(path), cancellationToken);

    public Task ClosePdbAsync(uint pdbId, CancellationToken cancellationToken = default) =>
        Enqueue(workspace =>
        {
            workspace.ClosePdb(pdbId);
            return true;
        }, cancellationToken);

    public Task<SymbolPage> QuerySymbolsAsync(
        uint pdbFilter,
        uint categories,
        string? searchText,
        uint offset,
        uint limit,
        CancellationToken cancellationToken = default) =>
        Enqueue(workspace => workspace.QuerySymbols(
            pdbFilter, categories, searchText, offset, limit), cancellationToken);

    public Task<LocatedSymbol> LocateSymbolAsync(
        uint pdbFilter,
        uint pdbId,
        uint symIndexId,
        uint categories,
        CancellationToken cancellationToken = default) =>
        Enqueue(workspace => workspace.LocateSymbol(
            pdbFilter, pdbId, symIndexId, categories), cancellationToken);

    public Task<SymbolDefinition> GetDefinitionAsync(
        uint pdbId,
        uint symIndexId,
        CancellationToken cancellationToken = default) =>
        Enqueue(workspace => workspace.GetDefinition(pdbId, symIndexId), cancellationToken);

    private Task<T> Enqueue<T>(Func<NativeSymbolWorkspace, T> operation, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.FromException<T>(new ObjectDisposedException(nameof(SymbolWorkspaceService)));
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(operation(_workspace!));
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        });

        return completion.Task.WaitAsync(cancellationToken);
    }

    private void ThreadMain()
    {
        try
        {
            _workspace = NativeSymbolWorkspace.Create();
            _ready.TrySetResult();
        }
        catch (Exception error)
        {
            _startupError = error;
            _ready.TrySetResult();
            return;
        }

        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }

        _workspace.Dispose();
        _workspace = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_thread.IsAlive)
        {
            _queue.CompleteAdding();
            await Task.Run(() => _thread.Join()).ConfigureAwait(false);
        }

        _queue.Dispose();
    }
}
