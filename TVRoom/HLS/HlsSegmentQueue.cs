using CommunityToolkit.HighPerformance;
using System.Collections;

namespace TVRoom.HLS
{
    public readonly struct HlsSegmentQueue : IReadOnlyList<HlsSegmentEntry>
    {
        private readonly HlsSegmentEntry[] _items;
        public int MaxSize { get; }

        public HlsSegmentQueue(int maxSize) : this(maxSize, Array.Empty<HlsSegmentEntry>()) 
        { 
        }

        private HlsSegmentQueue(int maxSize, HlsSegmentEntry[] items)
        {
            _items = items;
            MaxSize = maxSize;
        }

        public static HlsSegmentQueue Create(int maxSize, ReadOnlySpan<HlsSegmentEntry> items)
        {
            return new HlsSegmentQueue(maxSize, items.ToArray());
        }

        public HlsSegmentQueue Push(HlsSegmentEntry item, out HlsSegmentEntry? popped)
        {
            if (_items.Length < MaxSize)
            {
                var appended = new HlsSegmentEntry[_items.Length + 1];
                _items.CopyTo(appended, 0);
                appended[_items.Length] = item;
                popped = default;
                return new HlsSegmentQueue(MaxSize, appended);
            }

            var shifted = new HlsSegmentEntry[_items.Length];
            _items.AsSpan().Slice(1).CopyTo(shifted);
            shifted[_items.Length - 1] = item;
            popped = _items[0];
            return new HlsSegmentQueue(MaxSize, shifted);
        }

        public HlsSegmentQueue ReplaceLast(HlsSegmentEntry item)
        {
            var items = _items.ToArray();
            items[^1] = item;
            return new HlsSegmentQueue(MaxSize, items);
        }

        public HlsSegmentEntry this[int index] => _items[index];

        public int Count => _items.Length;

        IEnumerator<HlsSegmentEntry> IEnumerable<HlsSegmentEntry>.GetEnumerator()
        {
            return ((IEnumerable<HlsSegmentEntry>)_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }
}
