using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Internals;

public class FreePage_Tests
{
    private const string SkipReason = "Internal engine snapshot/transaction APIs removed in async redesign (Phase 2). Needs rewrite.";

    [Fact(Skip = SkipReason)]
    public void FreeSlot_Insert() { }

    [Fact(Skip = SkipReason)]
    public void FreeSlot_Delete() { }
}