using System;
using NUnit.Framework;

namespace Rebus.Msmq.Tests
{
    [TestFixture]
    public class TestMsmqUtil
    {
        [Test]
        public void CanGetCount()
        {
            var path = MsmqUtil.GetPath("global.dealcapture.metadata.input");

            Console.WriteLine($"Checking {path}");

            var count = MsmqUtil.GetCount(path);

            Assert.That(count, Is.EqualTo(29));
        }
    }
}