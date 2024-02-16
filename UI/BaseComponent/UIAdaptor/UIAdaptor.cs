using System;
using UnityEngine;
using UniRx;


namespace Seeder
{   
    public struct UIAdaptorEventData
    {
        public UIAdaptor Adaptor { get; set; }
        public TriggerData EvtData { get; set; }
    }

    // UI를 구성하는 기본 컴퍼넌트 기능을 중간에서 제어하는 스크립트.    
    public abstract class UIAdaptor : MonoCustom
    {
        // ui의 자신의 id.
        [SerializeField] protected string _id;
        [SerializeField] protected InputTrigger _trigger;
        [SerializeField] protected UIText _debugText;

        protected bool _initializedFirst;
        // 사운드 정보를 등록하기 위해 사용.
        protected AudioSource _showAudioSrc;

        public string Id { get { return _id; } set { _id = value; } }
        public bool IsForDebug { get { return !string.IsNullOrEmpty(_id) && _id.Contains(SystemName.debug); } }
        public InputTrigger Trigger
        { 
            get
            {
                if (_trigger == null)
                {
                    _trigger = GetComponent<InputTrigger>();
                }
                return _trigger; 
            }
        }
        public abstract bool TouchCollider { get; set; }
        // rx stream, 이벤트 관련 변수들.                
        private Subject<TouchEventData<UIAdaptor>> _sbjOnInputClick;
        private Subject<TouchEventData<UIAdaptor>> _sbjOnInputLong;
        private Subject<TouchEventData<UIAdaptor>> _sbjOnInputDown;
        private Subject<TouchEventData<UIAdaptor>> _sbjOnInputMove;
        private Subject<TouchEventData<UIAdaptor>> _sbjOnInputUp;        

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================                
        protected override void OnAwake()
        {
            base.OnAwake();

            if (_trigger == null)
            {
                _trigger = GetComponent<InputTrigger>();
            }
            if (_trigger != null)
            {
                // 터치 down 처리.
                _trigger.OnDownAsObservable()
                    .Subscribe(evtData => SendEvent(_sbjOnInputDown, evtData))
                    .AddTo(this);
                // 터치 move 처리.
                _trigger.OnMoveAsObservable()
                    .Subscribe(evtData => SendEvent(_sbjOnInputMove, evtData))
                    .AddTo(this);
                // 터치 up 처리.
                _trigger.OnUpAsObservable()
                    .Subscribe(evtData => SendEvent(_sbjOnInputUp, evtData))
                    .AddTo(this);

                // 터치 click 처리.
                _trigger.OnClickAsObservable()
                    .Subscribe(evtData => SendEvent(_sbjOnInputClick, evtData))
                    .AddTo(this);
                // 터치 long 처리.
                _trigger.OnLongAsObservable()
                    .Subscribe(evtData => SendEvent(_sbjOnInputLong, evtData))
                    .AddTo(this);
            }

            _showAudioSrc = GetComponent<AudioSource>();
            if (_showAudioSrc != null)
            {
                // 실제 재생은 나중에 제어하므로 비활성화 시킨다.
                _showAudioSrc.enabled = false;
                _showAudioSrc.Stop();
            }

            if (_debugText != null && !_debugText.IsForDebug)
            {
                _debugText.Id = SystemName.debug;
            }
            _debugText?.ActivateToAwake();
            _debugText?.SetActive(false);

            InitializeFirst();
        }

        //================================================================================
        // 디버그 및 테스트용 메서드 모음
        //================================================================================        
        public void SetDebugContent(string text)
        {
            _debugText?.SetText(text);
        }

        //================================================================================
        // rx stream 메서드 모음 
        //================================================================================
        public IObservable<TouchEventData<UIAdaptor>> OnClickAsObservable()
        {
            return _sbjOnInputClick ??= this.AddSubject<TouchEventData<UIAdaptor>>();
        }
        public IObservable<TouchEventData<UIAdaptor>> OnLongDownAsObservable()
        {
            return _sbjOnInputLong ??= this.AddSubject<TouchEventData<UIAdaptor>>();
        }

        public IObservable<TouchEventData<UIAdaptor>> OnInputDownAsObservable()
        {
            return _sbjOnInputDown ??= this.AddSubject<TouchEventData<UIAdaptor>>();
        }
        public IObservable<TouchEventData<UIAdaptor>> OnInputMoveAsObservable()
        {
            return _sbjOnInputMove ??= this.AddSubject<TouchEventData<UIAdaptor>>();
        }
        public IObservable<TouchEventData<UIAdaptor>> OnInputUpAsObservable()
        {
            return _sbjOnInputUp ??= this.AddSubject<TouchEventData<UIAdaptor>>();
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================ 
        // 다른 monobehavior(ex:UIModule)보다 UIAdaptor가 Awake 호출전에 특정 데이터를 설정해야 할 경우에 외부에서 호출.
        // 주로 Awake에서 계산되어 나오는 값(ex:애니메이션의 duration...)을 위부에서 참조할 경우에 이 함수를 먼저 호출해서 처리한다.
        public void InitializeFirst()
        {
            if (_initializedFirst) return;

            _initializedFirst = true;
            OnInitializeFirst();
        }

        private void SendEvent(Subject<TouchEventData<UIAdaptor>> sbj, TriggerData pointerData)
        {
            sbj?.OnNext(new TouchEventData<UIAdaptor>() { Target = this, Data = pointerData });
        }

        protected virtual void OnInitializeFirst() { }
    }
}
