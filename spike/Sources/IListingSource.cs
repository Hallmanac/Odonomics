using Spike.Models;

namespace Spike.Sources;

public interface IListingSource
{
    string Name { get; }

    Task<SourceRunResult> RunAsync(CancellationToken cancellationToken);
}
