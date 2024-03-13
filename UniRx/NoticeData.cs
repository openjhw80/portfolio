using System;
using System.Collections.Generic;
using UnityEngine;
using UniRx;


namespace Seeder
{
    // 특정 action을 할 수 있는 상태인지 알려주는 데 사용하는 공용 데이터 구조.
    public struct StateOnData
    {
        private string _targetId;

        public string Name { get; set; }    // Notification 이벤트의 등록 이름.
        // 대상의 id.
        public string TargetId
        {
            get { return _targetId.IsNullOrEmpty() ? string.Empty : _targetId; }
            set { _targetId = value; }
        }  
        public bool IsOn { get; set; }      // 특정 상태 On, Off.        
    }

    public struct NoticeData<T>
    {        
        // Notice 구독 이름. 같은 depth에서 발생하면 NameCause와 같다.
        public string Name { get; set; }
        // Notice가 발생한 원인 이름. 같은 depth에서 발생하면 Name과 같다.
        public string NameCause { get; set; }
        public T Target { get; set; }    // Notice 항목의 값 혹은 대상.
    }

    public static class NoticeTool
    {
        public static void OnInvoke<T>(this Action<NoticeData<T>> cbEvent, string evtName, T target)
        {
            if (cbEvent == null)
            {
                return;
            }

            var evtData = new NoticeData<T>()
            {
                Name = evtName,
                NameCause = evtName,
                Target = target,
            };
            cbEvent.Invoke(evtData);
        }

        public static void OnNext<T>(this Subject<NoticeData<T>> sbjEvent, string evtName, T target)
        {
            if (sbjEvent == null)
            {
                return;
            }

            var evtData = new NoticeData<T>()
            {
                Name = evtName,
                NameCause = evtName,
                Target = target,
            };
            sbjEvent.OnNext(evtData);
        }
    }
}
