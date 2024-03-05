using System;
using System.Collections.Generic;
using UnityEngine;


namespace Seeder
{
    public abstract class ObjectPoolBase<T> : ClassBase
    {
        private Queue<T> _standbyQ = new Queue<T>();
        private HashSet<T> _usedSet = new HashSet<T>();

        public string Id { get; protected set; } = string.Empty;
        public int CountUsable { get{ return _standbyQ.Count; } }
        public bool CanUse { get { return CountUsable > 0; } }        

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================       
        protected override void OnDispose(bool disposing)
        {
            // 사용중인 item 제거.
            foreach(var item in _usedSet)
            {
                OnRemoveItem(item);
            }
            _usedSet.Clear();

            foreach(var item in _standbyQ)
            {
                OnRemoveItem(item);
            }
            _standbyQ.Clear();
        }

        //================================================================================
        // 새로 정의한 일반 메서드 모음./
        //================================================================================
        // return과 혼동되지 않도록 T item 직접 추가는 pool 내부에서만 하고
        // 외부에서는 Create로 count만 전달하여 내부에서 생성해 추가하도록 한다.
        protected void Add(T item)
        {
            if (item == null || IsDisposed
            || _usedSet.Contains(item)
            || _standbyQ.Contains(item))
            {
                return;
            }

            _standbyQ.Enqueue(item);
            OnAdd(item);
        }
        
        public T Lend()
        {
            return Lend(true);
        }        
        public T Lend(bool locking)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException("ObjectPool was already disposed.");
            }

            if (!CanUse)
            {
                CreateItem();

                // 생성했어도 사용불가 상태면 default 반환.
                if (!CanUse)
                {
                    return default;
                }
            }           

            var item = _standbyQ.Dequeue();
            if (locking)
            {
                _usedSet.Add(item);
                OnReadyToLend(item);
            }
            // item이 일시적 대여(데이터유지 보장안됨)인 경우, 사용중에 넣지 않고 대기큐 마지막으로 넣는다.
            else
            {
                _standbyQ.Enqueue(item);
            }

            return item;
        }

        public void Return(T item)
        {
            if (item == null || IsDisposed)
            {
                return;
            }

            OnReadyToReturn(item);
            _usedSet.Remove(item);
            _standbyQ.Enqueue(item);
        }
        
        public abstract void CreateItem(int count = 0);
        protected virtual void OnAdd(T item) { }
        protected virtual void OnRemoveItem(T item) { }
        protected virtual void OnReadyToLend(T item) { }
        protected virtual void OnReadyToReturn(T item) { }
	}
}
