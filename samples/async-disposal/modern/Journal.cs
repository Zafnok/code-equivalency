using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Equiv.Samples.AsyncDisposal;

public class Journal
{
    public async Task<int> FlushAsync(IAsyncDisposable scope, int count)
    {
        int written;
        await using (scope)
        {
            written = count * 2;
        }

        return written;
    }

    public async Task<int> CloseAsync(IAsyncDisposable scope, int count)
    {
        await using IAsyncDisposable held = scope;
        return count + 2;
    }

    public async Task<int> LastAsync(IAsyncEnumerable<int> entries)
    {
        int last = 0;
        await foreach (int entry in entries)
        {
            last = entry * 2;
        }

        return last;
    }

    public async Task<int> FirstAsync(IAsyncEnumerable<int> entries)
    {
        await foreach (int entry in entries)
        {
            return entry - 1;
        }

        return 0;
    }
}
