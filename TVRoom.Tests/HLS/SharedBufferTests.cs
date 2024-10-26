using System.Buffers;
using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{
    [TestClass]
    public class SharedBufferTests
    {
        private byte[] _testData = "SomeData"u8.ToArray();
        private ReadOnlySequence<byte> TestSequence => new ReadOnlySequence<byte>(_testData);

        [TestMethod]
        public void Lease_ReturnsSpan()
        {
            using var buffer = SharedBuffer.Create(TestSequence);
            using var lease = buffer.Rent();

            CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
        }

        [TestMethod]
        public void DisposeBuffer_ReturnsBufferImmediatelyIfNoLeases()
        {
            var buffer = SharedBuffer.Create(TestSequence);
            buffer.Dispose();
            Assert.IsTrue(buffer.IsBufferDisposed);
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void Rent_ThrowsIfDisposed()
        {
            var buffer = SharedBuffer.Create(TestSequence);
            buffer.Dispose();
            buffer.Rent();
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void Lease_ThrowsIfDisposed()
        {
            using var buffer = SharedBuffer.Create(TestSequence);
            var lease = buffer.Rent();
            lease.Dispose();
            lease.GetSpan();
        }

        [TestMethod]
        public void Lease_DelaysDispose()
        {
            var buffer = SharedBuffer.Create(TestSequence);

            using (var lease = buffer.Rent())
            {
                buffer.Dispose();

                CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
                Assert.IsFalse(buffer.IsBufferDisposed);
            }

            Assert.IsTrue(buffer.IsBufferDisposed);
        }

        [TestMethod]
        public void Rent_AllowedMultipleTimes()
        {
            var buffer = SharedBuffer.Create(TestSequence);

            using (var lease = buffer.Rent())
            {
                CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
            }

            using (var lease = buffer.Rent())
            {
                CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
            }
        }

        [TestMethod]
        public void Rent_AllowedMultipleTimesSimultaneously()
        {
            var buffer = SharedBuffer.Create(TestSequence);

            using (var lease = buffer.Rent())
            using (var lease2 = buffer.Rent())
            {
                CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
                CollectionAssert.AreEqual(_testData, lease2.GetSpan().ToArray());
            }
        }

        [TestMethod]
        public void DisposeBuffer_ReturnsBufferWhenAllLeasesDisposed()
        {
            var buffer = SharedBuffer.Create(TestSequence);

            var lease = buffer.Rent();
            var lease2 = buffer.Rent();

            buffer.Dispose();
            Assert.IsFalse(buffer.IsBufferDisposed);
            CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());

            lease.Dispose();
            Assert.IsFalse(buffer.IsBufferDisposed);
            CollectionAssert.AreEqual(_testData, lease2.GetSpan().ToArray());

            lease2.Dispose();
            Assert.IsTrue(buffer.IsBufferDisposed);
        }
    }
}
