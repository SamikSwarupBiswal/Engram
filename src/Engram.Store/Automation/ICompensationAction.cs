using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public interface ICompensationAction
{
    Task ExecuteCompensationAsync(ExecutionContext context, CancellationToken ct);
}
