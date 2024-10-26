using System.Collections;

namespace TVRoom.HLS
{
    public readonly struct FixedQueue<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;
        public int MaxSize { get; }

        public FixedQueue(int size) : this(Array.Empty<T>(), size)
        {
        }

        private FixedQueue(T[] items, int size) 
        {
            _items = items;
            MaxSize = size;
        }

        public static FixedQueue<T> Create(int size, ReadOnlySpan<T> items)
        {
            return new FixedQueue<T>(items.ToArray(), size);
        }

        public FixedQueue<T> Push(T item, out T? popped)
        {
            if (_items.Length < MaxSize)
            {
                var appended = new T[_items.Length + 1];
                _items.CopyTo(appended, 0);
                appended[_items.Length] = item;
                popped = default;
                return new FixedQueue<T>(appended, MaxSize);
            }

            var shifted = new T[_items.Length];
            _items.AsSpan().Slice(1).CopyTo(shifted);
            shifted[_items.Length - 1] = item;
            popped = _items[0];
            return new FixedQueue<T>(shifted, MaxSize);
        }

        public ReadOnlySpan<T> AsSpan() => _items.AsSpan();

        public T this[int index] => _items[index];

        public int Count => _items.Length;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return ((IEnumerable<T>)_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }
}
