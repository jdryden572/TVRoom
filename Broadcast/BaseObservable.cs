namespace TVRoom.Broadcast
{
    public abstract class BaseObservable<T> : IObservable<T>
    {
        private readonly object _lock = new();
        private (Guid, IObserver<T>)[] _observers = Array.Empty<(Guid, IObserver<T>)>();

        private (Guid, IObserver<T>)[] Observers
        {
            get
            {
                lock (_lock)
                {
                    return _observers;
                }
            }
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            var key = Guid.NewGuid();
            int count;
            lock (_lock)
            {
                _observers = _observers.Append((key, observer)).ToArray();
                count = _observers.Length;
            }

            OnSubscriberCountChange(count);
            return new Unsubscriber(this, key);
        }

        private void Unsubscribe(Guid key)
        {
            int count;
            lock (_lock)
            {
                _observers = _observers.Where(o => o.Item1 != key).ToArray();
                count = _observers.Length;
            }

            OnSubscriberCountChange(count);
        }

        protected void Next(T value)
        {
            foreach (var (_, observer) in Observers)
            {
                observer.OnNext(value);
            }
        }

        protected void Error(Exception error)
        {
            foreach (var (_, observer) in Observers)
            {
                observer.OnError(error);
            }
        }

        protected void Complete()
        {
            foreach (var (_, observer) in Observers)
            {
                observer.OnCompleted();
            }
        }

        protected virtual void OnSubscriberCountChange(int count)
        {
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly BaseObservable<T> _parent;
            private readonly Guid _key;

            public Unsubscriber(BaseObservable<T> parent, Guid key)
            {
                _parent = parent;
                _key = key;
            }

            public void Dispose()
            {
                _parent.Unsubscribe(_key);
            }
        }
    }
}
