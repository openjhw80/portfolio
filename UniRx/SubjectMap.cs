using System;
using System.Collections.Generic;
using UniRx;


namespace Seeder
{
    public struct SubjectMap<T>
    {
        private Dictionary<string, Subject<T>> _sbjMap;

        public Subject<T> AddSubject(IAutoDisposer disposer, string key)
        {
            if (key.IsNullOrEmpty())
            {
                return null;
            }
            
            return disposer.AddSubject(key, ref _sbjMap);
        }

        public void OnNext(string key, T value)
        {
            if (_sbjMap == null || !_sbjMap.TryGetValue(key, out var sbj))
            {
                return;
            }

            sbj.OnNext(value);
        }

        public bool Exist(string key)
        {
            return _sbjMap != null && _sbjMap.ContainsKey(key);
        }
    }

    public struct SubjectMap<K,V>
    {
        private Dictionary<K, Subject<V>> _sbjMap;

        public Subject<V> AddSubject(IAutoDisposer disposer, K key)
        {
            return disposer.AddSubject(key, ref _sbjMap);
        }

        public void OnNext(K key, V value)
        {
            if (_sbjMap == null || !_sbjMap.TryGetValue(key, out var sbj))
            {
                return;
            }

            sbj.OnNext(value);
        }
    }
}
