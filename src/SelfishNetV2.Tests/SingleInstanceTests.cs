using SelfishNetV2.Services;
using Xunit;

namespace SelfishNetV2.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void SecondAcquireFails_UntilRelease()
    {
        string name = "SelfishNetV2.Tests." + Guid.NewGuid().ToString("N");
        try
        {
            Assert.True(SingleInstanceGuard.TryAcquire(name));
            Assert.False(SingleInstanceGuard.TryAcquire(name));

            SingleInstanceGuard.Release();
            Assert.True(SingleInstanceGuard.TryAcquire(name));
        }
        finally
        {
            SingleInstanceGuard.Release();
        }
    }
}
