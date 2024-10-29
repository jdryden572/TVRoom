using CommunityToolkit.HighPerformance;
using System.Collections;

namespace TVRoom.HLS
{
    public readonly struct HlsSegmentList : IReadOnlyList<HlsSegmentEntry>
    {
        private readonly HlsSegmentEntry[] _items;
        public int MaxSize { get; }

        public HlsSegmentList(int maxSize) : this(maxSize, Array.Empty<HlsSegmentEntry>()) 
        { 
        }

        private HlsSegmentList(int maxSize, HlsSegmentEntry[] items)
        {
            _items = items;
            MaxSize = maxSize;
        }

        public static HlsSegmentList Create(int maxSize, ReadOnlySpan<HlsSegmentEntry> items)
        {
            return new HlsSegmentList(maxSize, items.ToArray());
        }

        public HlsSegmentList Push(HlsSegmentEntry item, out HlsSegmentEntry? popped)
        {
            if (_items.Length < MaxSize)
            {
                var appended = new HlsSegmentEntry[_items.Length + 1];
                _items.CopyTo(appended, 0);
                appended[_items.Length] = item;
                popped = default;
                return new HlsSegmentList(MaxSize, appended);
            }

            var shifted = new HlsSegmentEntry[_items.Length];
            _items.AsSpan().Slice(1).CopyTo(shifted);
            shifted[_items.Length - 1] = item;
            popped = _items[0];
            return new HlsSegmentList(MaxSize, shifted);
        }

        public HlsSegmentList Replace(Index index, HlsSegmentEntry item)
        {
            var items = _items.ToArray();
            items[index] = item;
            return new HlsSegmentList(MaxSize, items);
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
