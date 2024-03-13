using System;
using System.Collections.Generic;
using UniRx;


namespace Seeder
{
    public struct NoticeSubject<T>
    {
        private Dictionary<string, Subject<NoticeData<T>>> _sbjNoticeSet;

        public Subject<NoticeData<T>> AddSubject(IAutoDisposer disposer, string rxName = RxName.any)
        {
            if (rxName.IsNullOrEmpty())
            {
                return null;
            }
            
            return disposer.AddSubject(rxName, ref _sbjNoticeSet);
        }
        public void OnNext(string name, string nameCause, T value)
        {
            if (_sbjNoticeSet == null)
            {
                return;
            }

            // 등록된 last이벤트 이름으로 보낸다.
            if (_sbjNoticeSet.TryGetValue(name, out var sbj))
            {
                var sbjData = new NoticeData<T>()
                {
                    Name = name,
                    NameCause = nameCause,
                    Target = value,
                };
                sbj.OnNext(sbjData);
            }

            // any로 한번 더 보낸다.
            if (_sbjNoticeSet.TryGetValue(RxName.any, out var sbjAny))
            {
                var sbjData = new NoticeData<T>()
                {
                    Name = name,
                    NameCause = nameCause,
                    Target = value,
                };
                sbjAny.OnNext(sbjData);
            }
        }
        public void OnNext(string nameCause, T value)
        {
            OnNext(nameCause, nameCause, value);
        }
        public void OnNext(NoticeData<T> data)
        {
            OnNext(data.Name, data.NameCause, data.Target);
        }
    }
}
