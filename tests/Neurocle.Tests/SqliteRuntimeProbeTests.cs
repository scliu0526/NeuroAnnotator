using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.Infrastructure.Persistence;

namespace Neurocle.Tests
{
    [TestClass]
    public class SqliteRuntimeProbeTests
    {
        [TestMethod]
        public void CanOpenInMemoryDatabase_returns_true()
        {
            Assert.IsTrue(SqliteRuntimeProbe.CanOpenInMemoryDatabase());
        }
    }
}
