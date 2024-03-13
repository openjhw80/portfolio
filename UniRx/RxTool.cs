using System;
using System.Collections.Generic;
using UniRx;


namespace Seeder
{    
    public static partial class RxTool
    {
        public static Subject<T> AddSubject<T>(this ClassBase classBase)
        {   
            var subject = new Subject<T>();
            subject.AddTo(classBase);
            return subject;
        }
        public static Subject<T> AddSubject<T>(this MonoCustom monoCustom)
        {   
            var subject = new Subject<T>();
            subject.AddTo(monoCustom);
            return subject;
        }
        public static Subject<T> AddSubject<T>(this IAutoDisposer autoDisposer)
        {
            var subject = new Subject<T>();
            subject.AddTo(autoDisposer);
            return subject;
        }

        public static Subject<V> AddSubject<K,V>(this IAutoDisposer owner, K key, ref Dictionary<K, Subject<V>> sbjSet)
        {            
            sbjSet ??= new Dictionary<K, Subject<V>>();
            if (!sbjSet.TryGetValue(key, out var sbj))
            {
                sbj = owner.AddSubject<V>();
                sbjSet.Add(key, sbj);
            }

            return sbj;
        }
    }
}
