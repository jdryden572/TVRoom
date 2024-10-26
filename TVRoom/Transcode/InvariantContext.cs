using System.Globalization;

namespace TVRoom.Transcode
{
    public readonly struct InvariantContext : IDisposable
    {
        private readonly CultureInfo _originalCulture;

        public InvariantContext()
        {
            _originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }
    }
}
