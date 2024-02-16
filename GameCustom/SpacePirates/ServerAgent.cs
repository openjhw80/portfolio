using System;
using UnityEngine;
using UnityEngine.Networking;
using UniRx.Async;
using Firebase.Firestore;


namespace Seeder
{
    public sealed partial class ServerAgent : ClassBase
    {        
        private static ServerAgent _srvAgent;
        private static bool _isActivated;

        public static DateTimeOffset Now { get { return _srvAgent._clock.GetNow(); } }
        public static DateTimeOffset UtcNow { get { return _srvAgent._clock.GetUtcNow(); } }        
        public static TimeSpan TimeZone { get { return _srvAgent._clock.GetTimeZone(); } }

        private Clock _clock;        

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================ 
        private ServerAgent() { }
        public static void Activate()
        {
            if (_isActivated)
            {
                return;
            }

            _isActivated = true;

            InitializeLogin();            

            // 파이어베이스 초기화.
            _ = FirebaseFirestore.DefaultInstance;

            _srvAgent = new ServerAgent();
            _srvAgent.Initialize();
        }

        private void Initialize()
        {
            // 기본 timeZone 문자열은 한국기준이다.
            _clock = new Clock("+09:00");

            Application.focusChanged += (hasFocus) =>
            {
                if(hasFocus)
                {
                    Debug.Log("앱 포커스가 돌아옴.");

                    CheckConnection(null);
                }
            };
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================                
        // 인터넷 연결 여부 체크. 성공하면 서버 시간도 갱신해준다.
        public async static void CheckConnection(Action<bool> cbResult)
        {   
            var isSuccess = await CheckConnectionAsync();
            // 콜백 처리.
            cbResult?.Invoke(isSuccess);
        }

        // 인터넷 연결 여부 체크. 성공하면 서버 시간도 갱신해준다.
        public async static UniTask<bool> CheckConnectionAsync()
        {
            var url = "www.google.com";
            Debug.Log($"인터넷에서 서버 시간 가져오기. url: {url}");

            using var webRequest = UnityWebRequest.Get(url);
            webRequest.timeout = 10;    // 인터넷 연결 대기 시간 10초.
            await webRequest.SendWebRequest().ToUniTask();
            
            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(webRequest.error);
                return false;
            }
            else
            {
                // 반송된 데이터에서 시간 데이터 가져오기.
                var dateStr = webRequest.GetResponseHeader("date");
                Debug.Log($"인터넷 서버시간: {dateStr}");

                // 서버 시간 sync 처리.
                if (_isActivated)
                {
                    var remoteDate = DateTime.Parse(dateStr);
                    _srvAgent._clock.Sync(remoteDate.ToUniversalTime());
                }
                
                return true;
            }
        }
    }
}
