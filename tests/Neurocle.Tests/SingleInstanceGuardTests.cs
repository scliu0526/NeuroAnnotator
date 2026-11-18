using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neurocle.WinForms;
using System.Threading;

namespace Neurocle.Tests
{
    [TestClass]
    public sealed class SingleInstanceGuardTests
    {
        [TestMethod]
        public void Second_guard_in_the_same_session_is_rejected_without_replacing_the_owner()
        {
            using (SingleInstanceGuard first = SingleInstanceGuard.TryAcquire())
            {
                Assert.IsNotNull(first);
                SingleInstanceGuard second = null;
                var thread = new Thread(() => second = SingleInstanceGuard.TryAcquire());
                thread.Start();
                thread.Join();
                Assert.IsNull(second);
            }
        }

        [TestMethod]
        public void Released_guard_can_be_acquired_again()
        {
            using (SingleInstanceGuard first = SingleInstanceGuard.TryAcquire())
                Assert.IsNotNull(first);

            using (SingleInstanceGuard second = SingleInstanceGuard.TryAcquire())
                Assert.IsNotNull(second);
        }

        [TestMethod]
        public void Mutex_name_is_local_to_the_windows_session()
        {
            StringAssert.StartsWith(SingleInstanceGuard.MutexName, @"Local\");
        }
    }
}
