using Cynara.Application.Forms;

namespace Cynara.Api.Tests;

public sealed class SnapshotContractTests
{
    [Fact]
    public async Task FormVersionLifecycle_PermittedTransitions()
    {
        IReadOnlyList<string> transitions =
            FormVersionLifecycle.DescribePermittedTransitions();
        await Verify(transitions);
    }
}
