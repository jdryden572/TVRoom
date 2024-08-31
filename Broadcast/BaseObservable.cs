using System.Collections.Concurrent;

namespace TVRoom.Broadcast
{
    public abstract class BaseObservable<T> : IObservable<T>
    {
        private readonly ConcurrentDictionary<Guid, IObserver<T>> _observers = new();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            var key = Guid.NewGuid();
            _observers.TryAdd(key, observer);
            return new Unsubscriber(_observers, key);
        }

        protected void Next(T value)
        {
            foreach (var observer in _observers.Values)
            {
                observer.OnNext(value);
            }
        }

        protected void Error(Exception error)
        {
            foreach (var observer in _observers.Values)
            {
                observer.OnError(error);
            }
        }

        protected void Complete()
        {
            foreach (var observer in _observers.Values)
            {
                observer.OnCompleted();
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly ConcurrentDictionary<Guid, IObserver<T>> _observers;
            private readonly Guid _key;

            public Unsubscriber(ConcurrentDictionary<Guid, IObserver<T>> observers, Guid key)
            {
                _observers = observers;
                _key = key;
            }

            public void Dispose()
            {
                
                _observers.TryRemove(_key, out _);
            }
        }
    }
}
