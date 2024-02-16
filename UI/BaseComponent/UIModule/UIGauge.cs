using System;
using UnityEngine;
using UnityEngine.UI;
using UniRx;


namespace Seeder
{
    // Gauge 처리를 담당하는 UIModule.
    public sealed class UIGauge : UIModule
    {
        // main게이지 관련 변수(기본)
        [SerializeField] private string _valueId;
        [SerializeField] private UIImageFill _uiMainImage;        
        [SerializeField] private UIText _uiText;
        // sub게이지 관련 변수(확장)
        [SerializeField] private UIImageFill _uiSubImage;
        [SerializeField][Tooltip("서브 게이지 감소가 시작하기전 딜레이(초).")]
        private float _subDelay;
        [SerializeField][Tooltip("서브 게이지 전체가 감소하는데 걸리는 시간(초). 즉 감소 속도.")]
        private float _subDuration;    

        private DataWatcher<double> _valueWatcher;
        private Tick _tickSubGauge;     
        public string ValueId { get { return _valueId; } }
        public string ValueNowId { get; private set; }
        public double ValueMax
        {
            get { return _valueWatcher.ValueMax; }
            set { if (_valueWatcher != null) _valueWatcher.ValueMax = value; }            
        }
        public double ValueNow
        {
            get { return _valueWatcher.ValueNow; }
            set { if (_valueWatcher != null) _valueWatcher.ValueNow = value; }
        }
        public float FillAmount
        {
            get { return _uiMainImage.FillAmount; }
        }

        // rx stream, 이벤트 관련 변수들.

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================                
        protected override void OnAwake()
        {
            base.OnAwake();
            
            _uiMainImage ??= GetComponentInChildren<UIImageFill>(true);            
            if (_uiMainImage == null)
            {
                var img = GetComponent<Image>();
                if (img != null)
                {
                    _uiMainImage = img.gameObject.AddComponent<UIImageFill>();
                }
            }            

            ValueNowId = _valueId.PutSuffix(AttrName.now);
            _valueWatcher = DataWatcher<double>.FromCB(_ => SetGauge());                
            _valueWatcher.AddTo(this);

            if(_uiSubImage != null)
            {
                // sub의 amount를 0으로 초기화.
                _uiSubImage.FillAmount = 0;
                // sub의 애니메이션을 위한 tick 생성.
                _tickSubGauge = new Tick(ETimeType.Scaled, this);                
                _tickSubGauge.OnUpdateTickAsObservable()
                    .Subscribe(tickData =>
                    {
                        var dtAmount = tickData.DeltaTime / _subDuration;
                        _uiSubImage.FillAmount -= dtAmount.Clamp(0, 1f);
                        // 서브 게이지의 amount가 메인게이지보다 작거나 같아지면 tick을 stop한다.
                        if (_uiSubImage.FillAmount <= _uiMainImage.FillAmount)
                        {
                            _uiSubImage.FillAmount = _uiMainImage.FillAmount;
                            _tickSubGauge.Stop();
                        }
                    })
                    .AddTo(this);
            }
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================        
        private void SetGauge()
        {
            if (ValueMax == 0) return;

            var preMainAmount = _uiMainImage.FillAmount;
            _uiMainImage.FillAmount = (float)(ValueNow / ValueMax);
            _uiMainImage.SetDebugContent((long)ValueNow, (long)ValueMax);

            SetSubGauge(preMainAmount);
        }

        private void SetSubGauge(float preMainAmount)
        {
            if(_uiSubImage == null)
            {
                return;
            }
            
            var mainAmount = _uiMainImage.FillAmount;
            // mainAmount값이 preMainAmount값보다 작으면 main게이지를 향해 sub게이지 애니메이션 실행.
            if (mainAmount < preMainAmount)
            {
                // 애니가 실행 중이 아니면 sub게이지 amount를 설정하고 tick을 실행한다.
                if(!_tickSubGauge.IsRun)
                {
                    _uiSubImage.FillAmount = preMainAmount;                    
                    _tickSubGauge.Restart(new Tick.StartParam() { Delay = _subDelay });
                }
            }
            // mainAmount값이 preMainAmount값보다 크면 sub게이지를 main에 맞추고 애니메이션을 멈춘다.
            else
            {   
                _uiSubImage.FillAmount = mainAmount;
                _tickSubGauge.Stop();
            }
        }

        public void SetEmpty()
        {
            _uiMainImage.FillAmount = 0;
            if (_uiSubImage != null)
            {
                _uiSubImage.FillAmount = 0;
            }
            _uiText?.SetEmpty();
        }

        public void SetGauge(double now, double max)
        {            
            ValueNow = now;
            ValueMax = max;            
        }

        public void SetText(string text)
        {
            _uiText?.SetText(text);
        }

        public void SetColor(Color? mainColor, Color? subColor = null)
        {
            if (mainColor.HasValue) _uiMainImage?.SetColor(mainColor.Value);
            if (subColor.HasValue) _uiSubImage?.SetColor(subColor.Value);
        }

        public void SetImage(string mainImgName, string subImgName = null)
        {
            if (!string.IsNullOrEmpty(mainImgName))
            {
                _uiMainImage?.SetImage(mainImgName);
            }
            if (!string.IsNullOrEmpty(subImgName))
            {
                _uiSubImage?.SetImage(subImgName);                
            }            
        }

        public void Subscribe(
            IObservable<double> streamValueNow,
            IObservable<double> streamValueMax)
        {
            _valueWatcher.Subscribe(streamValueNow, streamValueMax);            
        }
    }
}
