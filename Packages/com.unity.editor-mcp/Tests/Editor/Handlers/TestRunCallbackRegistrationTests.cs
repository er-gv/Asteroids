using NUnit.Framework;
using UnityEditorMCP.Handlers;

namespace UnityEditorMCP.Tests.Handlers
{
    [TestFixture]
    public class TestRunCallbackRegistrationTests
    {
        [Test]
        public void FirstRegistrationIsPerformed()
        {
            var registration = new TestRunCallbackRegistration();

            Assert.IsTrue(registration.ShouldRegister());
            Assert.IsTrue(registration.IsRegistered);
            Assert.AreEqual(1, registration.RegisterCount);
            Assert.AreEqual(1, registration.LiveRegistrations);
        }

        [Test]
        public void RepeatedEnsureCallsRegisterOnlyOnce()
        {
            var registration = new TestRunCallbackRegistration();
            registration.ShouldRegister();

            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(registration.ShouldRegister(), "only one registration may be live per domain");
            }

            Assert.AreEqual(1, registration.LiveRegistrations);
        }

        [Test]
        public void UnregisterIsOnlyPerformedWhenRegistered()
        {
            var registration = new TestRunCallbackRegistration();

            Assert.IsFalse(registration.ShouldUnregister(), "nothing to unregister yet");

            registration.ShouldRegister();
            Assert.IsTrue(registration.ShouldUnregister());
            Assert.IsFalse(registration.ShouldUnregister());
            Assert.AreEqual(0, registration.LiveRegistrations);
        }

        [Test]
        public void DomainReloadsNeverAccumulateRegistrations()
        {
            // Each domain gets a fresh tracker (statics are wiped), and beforeAssemblyReload releases
            // the outgoing one. Without the release, every reload added another live callback and each
            // test event was then reported once per reload.
            int totalRegistered = 0;
            int totalUnregistered = 0;

            for (int reload = 0; reload < 10; reload++)
            {
                var domain = new TestRunCallbackRegistration();

                if (domain.ShouldRegister())
                {
                    totalRegistered++;
                }

                // A second EnsureCallbacksRegistered in the same domain must be a no-op.
                Assert.IsFalse(domain.ShouldRegister());
                Assert.AreEqual(1, domain.LiveRegistrations);

                if (domain.ShouldUnregister())
                {
                    totalUnregistered++;
                }

                Assert.AreEqual(0, domain.LiveRegistrations, "the outgoing domain must leave nothing behind");
            }

            Assert.AreEqual(10, totalRegistered);
            Assert.AreEqual(totalRegistered, totalUnregistered);
        }
    }
}
