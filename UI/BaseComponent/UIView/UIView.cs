using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using UniRx;


namespace Seeder
{
    public struct UIDataDummy { }

    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public abstract class UIView : MonoCustom
    {
        // 공용 Param을 사용해야 하는 UI에 대한 param.
        public struct OpenParamCommon
        {
            public DataBlock Data { get; set; }
        }

        // ui 구별을 위한 id.
        [SerializeField] protected string _id;          
        [SerializeField] protected UIViewOrderLayer _orderLayer = UIViewOrderLayer.None;
        [SerializeField] protected bool _coverCompletely;        
        [SerializeField] protected AudioSource _openAudioSrc;

        protected Canvas _canvas;
        protected GraphicRaycaster _raycaster;
        protected CanvasGroup _canvasGroup;
        protected List<IAnimationEffector> _uiAniFxList = new List<IAnimationEffector>();
        protected List<ScrollRect> _scrollRectList = new List<ScrollRect>();
        protected int _loadCount;        
        protected string _openSoundName = ResName.snd_fx_popup_open;

        public RectTransform RectTf { get { return transform as RectTransform; } }        
        public string Id { get { return _id; } set { _id = value; } }        
        public UIViewOrderLayer OrderLayer { get { return _orderLayer; } set { _orderLayer = value; } }      
        public string SortingLayerName { get; set; }
        public int SortingOrder { get; set; }
        public bool IsCoveredCompletely { get { return _coverCompletely; } }
        public bool IsLoadComplete { get { return _loadCount <= 0; } }
        public bool IsOpened { get; private set; }
        public bool CanPlayOpening { get; protected set; }        
        public EUIViewState StateNow { get; protected set; }
        public bool IsOnNoti { get; protected set; }

        // rx stream, 이벤트 관련 변수들.        
        private IDisposable _openCloseTimerDisposer;
        private Subject<UIView> _sbjOpen;        
        private Subject<UIView> _sbjClose;
        private Subject<UIView> _sbjEndPlayOpening;

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================
        protected override void OnAwake()
        {
            base.OnAwake();
            
            // canvas 및 raycaster 설정.
            _canvas = GetComponent<Canvas>();
            _canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            _raycaster = GetComponent<GraphicRaycaster>();
            _raycaster.blockingMask = 0;
            _raycaster.blockingMask = 1 << LayerMask.NameToLayer("UI");
            _canvasGroup = GetComponent<CanvasGroup>();

            GetComponentsInChildren(true, _uiAniFxList);
            foreach (var uiAni in _uiAniFxList)
            {
                var uiAniMono = uiAni as MonoCustom;
                if (uiAniMono != null)
                {
                    uiAniMono.ActivateToAwake();
                }                
            }

            // 모든 이벤트 표시기 활성화.
            var evtIndicatorList = GetComponentsInChildren<UINoticeObserver>(true);
            foreach (var indicator in evtIndicatorList)
            {
                if (!indicator.IsAwaked)
                {
                    indicator.SetActive(true);
                }
            }            

            if (_openAudioSrc == null)
            {
                _openAudioSrc = GetComponent<AudioSource>();
            }            
            if (_openAudioSrc != null)
            {
                // 실제 재생은 SoundManager를 통해서 하므로 비활성화 시킨다.
                _openAudioSrc.enabled = false;
                _openAudioSrc.Stop();
            }

            // 스크롤이 자동으로 움직이지 않도록 활성화되는 순간에 멈춤.(1회성)
            GetComponentsInChildren(true, _scrollRectList);
            if (!_scrollRectList.IsNullOrEmpty())
            {
                IDisposable updateDisposer = null;
                updateDisposer = Observable.EveryUpdate()
                    .Where(_ => IsActive)
                    .Subscribe(_ =>
                    {
                        for (var i = _scrollRectList.Count - 1; i >= 0; i--)
                        {
                            var sr = _scrollRectList[i];
                            if (sr.isActiveAndEnabled)
                            {
                                // 스크롤이 활성화 되는 순간, 리스트에서 삭제하고 다음 프레임에 좌상단 위치로 가도록 강제 설정.
                                _scrollRectList.RemoveAt(i);
                                Observable.TimerFrame(1)
                                    .Subscribe(_ =>
                                    {
                                        sr.normalizedPosition = Vector2.up;
                                        sr.StopMovement();
                                    });
                            }
                        }

                        if (_scrollRectList.Count == 0)
                        {
                            updateDisposer.RemoveTo(this, true);
                            updateDisposer = null;
                        }
                    })
                    .AddTo(this);
            }
        }

        protected override void OnStart()
        {
            base.OnStart();

            if (IsOpened)
            {
                OpenCore();
            }        
        }

        protected override void OnOnDisable()
        {
            base.OnOnDisable();

            // UI는 Destroy되지 않고 Scene마다 남아서 공유되어 사용될 수 있으므로
            // 비활성화시에 특정 동작 타이머(ex:오프닝 애니메이션...)를 clear해준다.
            ClearTimer();
        }

        //================================================================================
        // rx stream 메서드 모음 
        //================================================================================ 
        public IObservable<UIView> OnOpenAsObservable()
        {
            return _sbjOpen ??= this.AddSubject<UIView>();
        }

        public IObservable<UIView> OnCloseAsObservable()
        {
            return _sbjClose ??= this.AddSubject<UIView>();
        }

        public IObservable<UIView> OnEndPlayOpeningAsObservable()
        {
            return _sbjEndPlayOpening ??= this.AddSubject<UIView>();
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================        
        public void SetSorting(string layerName, int order)
        {
            SortingLayerName = layerName;
            SortingOrder = order;

            // canvas에 적용하려면 Active 상태가 되어 있어야 한다.
            if (IsActive)
            {
                SetSorting();
            }   
        }
        private void SetSorting()
        {            
            _canvas.overrideSorting = true;
            _canvas.sortingLayerName = SortingLayerName;
            _canvas.sortingOrder = SortingOrder;

            OnSetSorting();
        }
        private void ClearTimer()
        {
            _openCloseTimerDisposer.RemoveTo(this);
            _openCloseTimerDisposer = null;
        }

        public void Open()
        {
            Open(new UIDataDummy());
        }
        // UIView에 데이터를 설정하고 첫 Show를 처리하기 위한 메서드.
        public void Open<T>(T uiData) where T : struct
        {
            // 유효한 스냅샷 view가 아니면 스냅샷 데이터를 초기화 한다.
            if (!UIManager.Instance.IsValidSnapView(_id))
            {
                ClearSnapShot();
            }

            IsOpened = true;
            CanPlayOpening = true;

            SetActive(true);            
            OnOpen(new T?(uiData));

            // OnOpen로 전달한 파라미터로 설정 후, 실제 데이터 설정 메서드 실행.
            // 아직 Start 호출 전이면 Start에서 호출하도록 처리함.
            if (IsStarted)
            {
                OpenCore();
            }                        

            _sbjOpen?.OnNext(this);
        }        
        private void OpenCore()
        {
            // ui 데이터 설정.
            SetData();
            // canvas에 적용하려면 SetActive(true) 이후에 호출 해야 한다.
            SetSorting();
            // 데이터 설정 후, 스냅데이터를 지운다.
            ClearSnapShot();

            // open애니메이션 실행.
            ClearTimer();
            var duration = PlayOpening();
            if (CanPlayOpening && duration > 0)
            {
                _openCloseTimerDisposer = Observable.Timer(TimeSpan.FromSeconds(duration))
                    .Subscribe(_ =>
                    {
                        // 이후 코드에 Timer 관련 호출이 되더라도 문제가 없도록 제일 먼저 처리한다.
                        _openCloseTimerDisposer.RemoveTo(this, false);
                        _openCloseTimerDisposer = null;

                        OnFinishOpening();
                        _sbjEndPlayOpening?.OnNext(this);
                    })
                    .AddTo(this);
            }
            else
            {
                OnFinishOpening();
                _sbjEndPlayOpening?.OnNext(this);
            }

            // UI Open 사운드 재생.
            if (_openAudioSrc != null)
            {
                SystemCenter.SoundMgr.PlaySFX(_openAudioSrc);
            }
            else
            {
                SystemCenter.SoundMgr.PlaySFX(_openSoundName);
            }

            // canvasGroup의 알파가 켜져 있어야 한다.
            if (_coverCompletely && (_canvasGroup == null || _canvasGroup.alpha == 1))
            {
                GameRequest.SetFullCoveredMode(true);
            }
        }
        
        // 다시 열수 있도록 상태를 clear. 보통 Scene에서 같이 열려 있던 경우에 호출.
        public void CloseImmediately()
        {
            Close(new UIDataDummy(), false);
        }
        public void Close()
        {
            Close(new UIDataDummy());
        }
        public void Close<T>(T closeData, bool useAnimation = true) where T : struct
        {
            if(!IsOpened)
            {
                return;
            }

            IsOpened = false;

            // close 애니메이션 처리.
            ClearTimer();
            var duration = PlayClosing(new T?(closeData));
            if (useAnimation && duration > 0)
            {
                _openCloseTimerDisposer = Observable.Timer(TimeSpan.FromSeconds(duration))
                    .Subscribe(_ =>
                    {
                        // 이후 코드에 Timer 관련 호출이 되더라도 문제가 없도록 제일 먼저 처리한다.
                        _openCloseTimerDisposer.RemoveTo(this, false);
                        _openCloseTimerDisposer = null;

                        OnClose(new T?(closeData));
                        SetActive(false);                        

                        _sbjClose?.OnNext(this);

                    });                    
            }
            else
            {
                OnClose(new T?(closeData));
                SetActive(false);

                _sbjClose?.OnNext(this);                
            }
        }        

        public void ReOpen()
        {
            Show();
            OnReOpen();
        }

        public void Show(bool isShow)
        {
            if (isShow) Show();
            else Hide();
        }
        public void Show()
        {
            SetActive(true);            
        }

        public void Hide()
        {            
            SetActive(false);
        }       

        // back key를 눌렀을 때, 동작 처리.
        public virtual void Back()
        {
            Close();
        }

        // view가 open될때, 기본적으로 실행가능한 애니메이션
        protected float PlayOpeningBase()
        {
            float duration = 0;
            foreach (var uiAni in _uiAniFxList)
            {
                if (uiAni.IsAutoPlayWhenOpen)
                {
                    uiAni.Play();
                    duration = duration < uiAni.Duration ? uiAni.Duration : duration;
                }
            }

            return duration;
        }

        // 현재 UIView에서 사용하는 UI를 로드하는 메서드.
        // 기본적으로 autoRelease로 등록되어 UIView가 Destroy될 때 자동해제 되지만
        // 생성한 UI를 다른 곳으로 제어권을 넘겨줄 경우에는 autoRelease를 false로 하여 UIView가 사라져도 UI가 해제되지 않도록 한다.
        protected void LoadUI(ObjectLoadSetting setting,
            Action<AsyncOperationHandle<GameObject>> cbLoadEach = null, bool autoRelease = true)
        {
            if(!setting.IsValid)
            {
                return;
            }
            
            _loadCount += setting.Count;
            for (int i = 0; i < setting.Count; i++)
            {
                Addressables.InstantiateAsync(setting.Name, setting.Parent).Completed += (handler) =>
                {
                    if (autoRelease)
                    {
                        handler.AddTo(this);
                    }
                    
                    _loadCount -= 1;
                    cbLoadEach?.Invoke(handler);
                    if(IsLoadComplete)
                    {
                        OnLoadComplete();
                    }
                };
            }
        }
        
        protected virtual void OnOpen<T>(T uiData) { }        
        protected virtual void SetData() { }
        protected virtual float PlayOpening() { return PlayOpeningBase(); }
        protected virtual void OnFinishOpening() { }        
        protected virtual float PlayClosing<T>(T closeData) { return 0; }
        protected virtual void OnClose<T>(T closeData) { }        
        protected virtual void OnReOpen() { }
        protected virtual void OnLoadComplete() { }
        protected virtual void OnSetSorting() { }

        //=== UI의 현 상태 저장 및 복구를 위한 메서드 모음.
        public virtual void ClearSnapShot() { }
        public virtual void SetSnapShot() { }        
    }
}
