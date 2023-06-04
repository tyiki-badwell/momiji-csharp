using Microsoft.Extensions.Logging;
using Xunit;

namespace Momiji.Core.Buffer;

public class BufferPoolUnitTest
{
    internal class DummyItem : IDisposable
    {
        public void Dispose()
        {
            // NOP
        }
    }

    [Fact]
    public void Test1()
    {
        using var loggerFactory = new LoggerFactory();
        using var test = new BufferPool<DummyItem>(1, () => new DummyItem(), loggerFactory);
    }
}
