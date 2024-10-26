using System.Globalization;

namespace TVRoom.HLS
{
    public readonly struct CultureContext : IDisposable
    {
        private readonly CultureInfo _originalCulture;

        public CultureContext(CultureInfo culture)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }
    }
}
