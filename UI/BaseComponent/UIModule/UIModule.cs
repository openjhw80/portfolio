using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UniRx;


namespace Seeder
{
    public struct UIModuleEventData
    {
        public UIModule Module { get; set; }
        public PointerEventData EvtData { get; set; }
    }

    // UI표시를 위한 적절한 adaptor를 선택하고 게임내 개체의 데이터와 연동을 처리하는 클래스.
    public abstract class UIModule : MonoCustom
    {
        [SerializeField] protected string _assetName;
        // ui 구별을 위한 id.
        [SerializeField] protected string _id;
        // ui module에서 main trigger로 사용될 ui. 일반적으로 main UIImage를 지정한다.
        // 만약 커스텀 UI영역을 trigger로 지정하고 싶다면 투명이미지를 만들어 지정하면 된다.(일종의 collider 역할)
        [SerializeField] protected UIAdaptor _mainTrigger;
        [SerializeField] protected string _actionName;  // trigger로 터치시, action 처리를 위한 action 고유 이름.
        
        private bool _reservedSetData;

        public virtual string Id { get { return _id; } set { _id = value; } }
        public virtual bool IsEmpty { get { return false; } }
        public string TriggerAction { get { return _actionName; } }        
        // rx stream, 이벤트 관련 변수들.                
        protected Subject<TouchEventData<UIModule>> _sbjOnInputClick;
        protected Subject<TouchEventData<UIModule>> _sbjOnInputLong;

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================
        protected override void OnAwake()
        {
            base.OnAwake();

            if (_mainTrigger != null)
            {
                // 클릭 이벤트 구독.
                _mainTrigger.OnClickAsObservable()
                    .Where(_ => _mainTrigger.IsActive)
                    .Subscribe(evtData =>
                    {
                        SystemCenter.SoundMgr.PlaySFX(ResName.snd_fx_btn_click);
                        OnClick(evtData);
                    })
                    .AddTo(this);

                // 롱터치 이벤트 구독.
                _mainTrigger.OnLongDownAsObservable()
                    .Where(_ => _mainTrigger.IsActive)
                    .Subscribe(evtData => OnLong(evtData))
                    .AddTo(this);
            }
        }

        protected override void OnStart()
        {
            base.OnStart();

            if (_reservedSetData)
            {
                _reservedSetData = false;
                SetData();                
            }
        }

        //================================================================================
        // rx stream 메서드 모음 
        //================================================================================        
        public IObservable<TouchEventData<UIModule>> OnClickAsObservable()
        {
            return _sbjOnInputClick ??= this.AddSubject<TouchEventData<UIModule>>();
        }
        protected void OnNextClick(TriggerData triggerData)
        {   
            _sbjOnInputClick?.OnNext(new TouchEventData<UIModule>() { Target = this, Data = triggerData });
        }

        public IObservable<TouchEventData<UIModule>> OnLongDownAsObservable()
        {
            return _sbjOnInputLong ??= this.AddSubject<TouchEventData<UIModule>>();
        }
        protected void OnNextLong(TriggerData triggerData)
        {
            if (_sbjOnInputLong == null) return;

            SystemCenter.SoundMgr.PlaySFX(ResName.snd_fx_btn_click);
            _sbjOnInputLong?.OnNext(new TouchEventData<UIModule>() { Target = this, Data = triggerData });
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================    
        public void Clear()
        {
            OnClear();
        }

        // 강제 클릭 이벤트 발생. 해당 UIFlow를 진행을 위한 이벤트를 강제 발생시 사용.
        public void FireClick()
        {
            var tData = new TriggerData()
            {
                State = EPointerState.Click,
                PointerData = null,
            };
            var evtData = new TouchEventData<UIAdaptor>() { Target = _mainTrigger, Data = tData };
            OnClick(evtData);
        }
        
        public void SetMainTrigger(UIAdaptor uiAdaptor)
        {
            if (uiAdaptor == null || _mainTrigger != null)
            {
                return;
            }

            _mainTrigger = uiAdaptor;
            _mainTrigger.OnClickAsObservable()
                .Subscribe(evtData =>
                {
                    SystemCenter.SoundMgr.PlaySFX(ResName.snd_fx_btn_click);
                    OnClick(evtData);
                })
                .AddTo(this);
        }

        public void SetTouchable(bool isOn)
        {
            if (_mainTrigger == null)
            {
                return;
            }

            _mainTrigger.TouchCollider = isOn;            
        }

        public void SetTriggerActive(bool isActive)
        {
            if (_mainTrigger == null)
            {
                return;
            }
            
            _mainTrigger.SetActive(isActive);
        }

        public void SetUp(bool autoActive = true)
        {
            SetUp(new UIDataDummy(), autoActive);
        }

        public void SetUp<T>(T uiData, bool autoActive = true) where T : struct
        {
            if (autoActive)
            {
                SetActive(true);
            }
            
            OnSetUp(new T?(uiData));

            // OnSetData로 전달 파라미터로 설정 후, 실제 데이터 설정 메서드 실행.
            if (IsStarted)
            {
                SetData();                
            }
            else
            {
                _reservedSetData = true;
            }
        }

        //================================================================================
        // 이벤트 및 콜백 처리
        //================================================================================        
        protected virtual void OnClear() { }
        protected virtual void OnSetUp<T>(T uiData) { }
        protected virtual void OnClick(TouchEventData<UIAdaptor> evtData) { OnNextClick(evtData.Data); }
        protected virtual void OnLong(TouchEventData<UIAdaptor> evtData) { OnNextLong(evtData.Data); }
        protected virtual void SetData() { }
    }
}
