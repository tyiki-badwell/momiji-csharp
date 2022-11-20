using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Buffer;

[TestClass]
public class BufferPoolUnitTest
{
    internal class DummyItem : IDisposable
    {
        public void Dispose()
        {
            // NOP
        }
    }

    [TestMethod]
    public void Test1()
    {
        using var loggerFactory = new LoggerFactory();
        using var test = new BufferPool<DummyItem>(1, () => new DummyItem(), loggerFactory);
    }
}
