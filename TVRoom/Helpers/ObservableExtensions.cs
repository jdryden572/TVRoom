using System.Reactive.Subjects;

namespace TVRoom.Helpers
{
    public static class ObservableExtensions
    {
        public static IObservable<TOut> ConditionalMap<TIn, TOut>(this IObservable<TIn> source, Action<TIn, Action<TOut>> filterMap)
        {
            var subject = new Subject<TOut>();
            source.Subscribe(
                val => filterMap(val, subject.OnNext),
                subject.OnError,
                subject.OnCompleted);

            return subject;
        }
    }
}
