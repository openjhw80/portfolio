using System;
using UnityEngine;


namespace Seeder
{
    public class Clock: ClassBase
    {
        private TimeSpan _timeZone;
        private DateTimeOffset _timeBase;          
        private long _tickBase;

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================         
        public Clock() 
        {
            Initialize(string.Empty);
        }   

        public Clock(string timeZoneStr)
        {
            Initialize(timeZoneStr);
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        private void Initialize(string timeZoneStr)
        {
            var tzStr = timeZoneStr.IsNullOrEmpty() ? "+00:00" : timeZoneStr;
            _timeZone = DateTimeOffset.ParseExact(tzStr, "zzz", System.Globalization.CultureInfo.InvariantCulture).Offset;

            Sync(DateTime.UtcNow);
        }

        public void Sync(DateTime remoteNow)
        {
            // remote utc와 현재 플랫폼의 local utc를 비교해서 일정시간 이상 차이나면 전달된 utc로 재설정.
            var localNow = DateTime.UtcNow;
            var tsNow = localNow - remoteNow;
            var selectedNow = Math.Abs(tsNow.TotalSeconds) < 10 ? localNow : remoteNow;
            Debug.Log($"remote:{remoteNow}, local:{localNow}, tsSeconds:{tsNow.TotalSeconds}");

            // 시간 재설정.
            var timeZoneNow = selectedNow + _timeZone;
            _timeBase = new DateTimeOffset(timeZoneNow.Ticks, _timeZone);
            _tickBase = localNow.Ticks;
        }

        public TimeSpan GetElapsedTime()
        {
            var diffTick = DateTime.UtcNow.Ticks - _tickBase;
            return diffTick > 0 ? new TimeSpan(diffTick) : new TimeSpan(0);
        }        
        
        public DateTimeOffset GetNow()
        {
            return _timeBase + GetElapsedTime();             
        }

        public DateTimeOffset GetUtcNow()
        {
            var resultTime = GetNow();
            return resultTime.ToUniversalTime();
        }

        public TimeSpan GetTimeZone()
        {
            return _timeZone;
        }
    }
}
