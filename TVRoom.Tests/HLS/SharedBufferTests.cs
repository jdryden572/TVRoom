using Microsoft.Extensions.Logging;
using System.Buffers;
using TVRoom.HLS;

namespace TVRoom.Tests.HLS
{
    [TestClass]
    public class SharedBufferTests
    {
        private static ILogger _logger = new LoggerFactory().CreateLogger<SharedBuffer>();
        private static readonly byte[] _testData = "SomeData"u8.ToArray();

        private SharedBuffer CreateSharedBuffer() => SharedBuffer.Create(new ReadOnlySequence<byte>(_testData), logger: _logger);

        [TestMethod]
        public void Lease_ReturnsSpan()
        {
            using var buffer = CreateSharedBuffer();
            using var lease = buffer.Rent();

            CollectionAssert.AreEqual(_testData, lease.GetSpan().ToArray());
        }

        [TestMethod]
        public void DisposeBuffer_ReturnsBufferImmediatelyIfNoLeases()
        {
            var buffer = CreateSharedBuffer();
            buffer.Dispose();
            Assert.IsTrue(buffer.IsBufferDisposed);
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void Rent_ThrowsIfDisposed()
        {
            var buffer = CreateSharedBuffer();
            buffer.Dispose();
            buffer.Rent();
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void Lease_ThrowsIfDisposed()
        {
            using var buffer = CreateSharedBuffer();
            var lease = buffer.Rent();
            lease.Dispose();
            lease.GetSpan();
        }

        [TestMethod]
        public void Lease_DelaysDispose()
        {
            var buffer = CreateSharedBuffer();

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
            var buffer = CreateSharedBuffer();

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
            var buffer = CreateSharedBuffer();

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
            var buffer = CreateSharedBuffer();

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
