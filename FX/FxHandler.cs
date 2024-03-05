using System;
using UnityEngine;
using UniRx;


namespace Seeder
{  
    // effect 제어를 위한 handler.
    public abstract class FxHandler : MonoCustom, IReturnable
    {
        protected struct DummyData { }
        
        [SerializeField] protected string _id;
        [SerializeField] protected Vector3 _offsetPosMax;
        [SerializeField] protected Vector3 _offsetPosMin;
        // 특정 fx에서 고정위치를 사용하는 경우에 여기 값을 사용.
        [SerializeField] protected Vector3 _fixedPos;

        /* 부모 오브젝트를 follow하는지 여부.
         * true면 부모의 sorting이 변경되는 것에 맞춰서 자동으로 변경되며 항시 부모 order에 +1로 설정된다.
         */        
        protected bool _follower;                
        // 사운드 정보를 등록하기 위해 사용.
        protected AudioSource _showAudioSrc;
        protected Vector3 _scaleOriginal;
        // position 정보.
        protected Vector3 _movePosStart;
        protected Vector3 _movePosEnd;
        // 이동 시간 정보.
        protected float _moveTimeStart;
        protected float _moveTimeEnd;
        // 타이머.
        protected Tick _hideTimer;
        protected Tick _moveTimer;

        public string Id { get { return _id; } set { _id = value; } }                
        public Vector3 OffsetPosMin { get { return _offsetPosMin; } }
        public Vector3 OffsetPosMax { get { return _offsetPosMax; } }
        public Vector3 FixedPos { get { return _fixedPos; } }
        public float MoveDuration { get { return _moveTimeEnd - _moveTimeStart; } }
        public bool IsMove { get { return _movePosStart != _movePosEnd; } }
        public bool IsRun { get; private set; }
        

        // rx stream, 이벤트 관련 변수들.
        protected Action<FxHandler> _cbReturn;  // 소유자(ex:pool)에게 Hide 이후, 반납하기 위한 callback.
        protected Action<FxHandler> _cbAction;  // 이펙트의 ActionStart 발동에 대한 callback.
        protected Action<FxHandler> _cbHide;           

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================   
        protected override void OnAwake()
        {   
            base.OnAwake();

            _scaleOriginal = transform.localScale;

            _showAudioSrc = GetComponent<AudioSource>();
            if (_showAudioSrc != null)
            {
                // 실제 재생은 나중에 제어하므로 비활성화 시킨다.
                _showAudioSrc.enabled = false;
                _showAudioSrc.Stop();
            }
            
            _hideTimer = new Tick(ETimeType.Scaled, this);            
            _hideTimer.OnUpdateTickAsObservable()
                .Where(tickData => tickData.IsTimeUp)
                .Subscribe(_ => StopImmediately())
                .AddTo(this);                

            _useAutoInactive = true;
        }

        //================================================================================
        // override 메서드.
        //================================================================================                

        //================================================================================
        // interface 메서드 모음
        //================================================================================
        //===== IReturnable =====//
        void IReturnable.Return()
        {            
            if (IsRun)
            {
                // 실행중이면 중지시키고 내부에서 자동 회수.
                StopImmediately();
            }
            else
            {
                // 실행중이 아니면 바로 회수.
                _cbReturn?.Invoke(this);
                Clear();
            }
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        private void Clear()
        {
            // 각 설정값 리셋.
            _cbHide = null;
            _cbAction = null;
            _movePosStart = Vector3.zero;
            _movePosEnd = Vector3.zero;
            _moveTimeStart = 0;
            _moveTimeEnd = 0;

            transform.localScale = _scaleOriginal;
        }

        public void SetReturnCB(Action<FxHandler> cb)
        {
            _cbReturn = cb;
        }
        public FxHandler SetActionCB(Action<FxHandler> cb)
        {
            _cbAction = cb;
            return this;
        }
        public FxHandler SetHideCB(Action<FxHandler> cb)
        {
            _cbHide = cb;
            return this;
        }

        public void Run()
        {
            Run(new DummyData());
        }
        public void Run<T>(T setting) where T : struct
        {
            SetActive(true);

            IsRun = true;

            // show 사운드 재생.
            SystemCenter.SoundMgr.PlaySFX(_showAudioSrc);            

            OnRun(new T?(setting));

            // OnRun 하위에서 애니메이션 설정 후, move 이벤트 time을 설정해야 하므로 OnRun호출하고 처리한다.
            Move();
        }

        protected void Move()
        {
            // 시작과 끝이 같으면 이동하지 않는다.
            if (!IsMove)
            {
                return;
            }

            if (_moveTimer == null)
            {
                _moveTimer = new Tick(ETimeType.Scaled, this);                
                _moveTimer.OnUpdateTickAsObservable()                    
                    .Subscribe(tickData =>
                    {
                        var rateMove = tickData.ElapsedTime / MoveDuration;
                        var newPos = Vector3.Lerp(_movePosStart, _movePosEnd, rateMove);
                        transform.position = newPos;
                        SetSorting(-(int)newPos.y);                        

                        if (tickData.IsTimeUp)
                        {
                            _movePosEnd = _movePosStart;
                            _moveTimeStart = 0;
                            _moveTimeEnd = 0;
                        }
                    })
                    .AddTo(this);
            }

            _moveTimer.Clear();
            _moveTimer.LimitTime = _moveTimeEnd;
            _moveTimer.Start(new Tick.StartParam() { Delay = _moveTimeStart });
        }

        public void Stop(bool immediately = false)
        {
            if (immediately)
            {
                StopImmediately();
            }
            else
            {
                Stop(new DummyData());
            }
        }
        public void Stop<T>(T setting) where T : struct
        {
            if(!IsRun)
            {
                return;
            }

            OnStop(new T?(setting));
        }
        protected void StopImmediately()
        {
            if (!IsRun)
            {
                return;
            }                                    
            
            IsRun = false;

            // 내부 데이터 정리 및 동작 멈춤.
            _hideTimer.Stop();
            _moveTimer?.Stop();

            OnFinish();
            SetActive(false);            

            // 숨김 완료 알림.
            _cbHide?.Invoke(this);
            // 회수.
            _cbReturn?.Invoke(this);

            Clear();            
        }

        protected void SetHideTimer(float duration)
        {
            _hideTimer.Clear();

            // duration이 0보다 크면 loop가 아닌 것으로 가정하고 타이머를 실행시킨다.
            if (duration > 0)
            {   
                _hideTimer.LimitTime = duration;
                _hideTimer.Start();
            }
        }

        protected FxHandler SetToFollower(Transform followTarget)
        {
            if (followTarget == null)
            {
                _follower = false;
                return this;
            }

            _follower = true;            
            OnSetFollower(followTarget.GetComponentInChildren<SpinePlayer>(true));

            return this;
        }

        // 부모 설정이 있을 경우, localposition이나 scale은 부모 설정이 끝난 후에 적용해야 올바르게 설정된다.
        public FxHandler SetParent(Transform parent, Transform followTarget = null)
        {
            transform.SetParent(parent);            
            SetToFollower(followTarget);
            // 부모의 하위로 배치할 때, scale을 원래의 크기로 맞춰 설정해준다.
            transform.localScale = _scaleOriginal;

            return this;
        }

        public FxHandler SetLocalPosition(Vector3 pos)
        {
            _movePosStart = pos;
            _movePosEnd = pos;
            transform.localPosition = pos;

            OnSetPosition();

            return this;
        }

        public FxHandler SetPosition(Vector3 pos)
        {
            return SetPosition(pos, pos);
        }
        public FxHandler SetPosition(Vector3 startPos, Vector3 endPos)
        {
            _movePosStart = startPos;
            _movePosEnd = endPos;
            transform.position = startPos;

            OnSetPosition();

            return this;
        }

        public FxHandler SetMoveTime(float startTime, float endTime)
        {
            _moveTimeStart = startTime;
            _moveTimeEnd = endTime;

            return this;
        }
        
        public FxHandler SetSorting(int? order)
        {
            return SetSorting(null, order);
        }
        public FxHandler SetSorting(string layer, int? order)
        {
            if(!_follower)
            {
                OnSetSorting(layer, order);
            }

            return this;
        }

        protected virtual void OnRun<T>(T setting) { }
        protected virtual void OnStop<T>(T setting) { StopImmediately(); }
        protected virtual void OnFinish() { }   // Hide가 완전 마무리된 상태를 전달하여 내부 데이터 정리를 하기 위해 사용.
        protected virtual void OnSetFollower(SpinePlayer target) { }
        protected virtual void OnSetPosition() { }        
        protected virtual void OnSetSorting(string layer, int? order) { }        
    }
}
