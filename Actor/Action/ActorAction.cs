using System;
using System.Collections.ObjectModel;
using UniRx;


namespace Seeder
{
    public abstract class ActorAction : ClassBase
    {
        protected ActionInitParams _initSetting;            
        // 모든 액션이 완료되었을 때, 콜백 알림. 콜백 받은 곳(state)에서 다음행동을 지정하게 한다.
        protected Action<ActorAction> _cbComplete;   

        //=== 변동없는 고정값 ===//     
        public Actor Actor { get { return _initSetting.Actor; } }
        public string Category { get { return _initSetting.Category; } }
        public string Name { get { return _initSetting.Name; } }
        // 같은 액션이 실행될 때, 액션을 다시 Start 할 것인지 여부.
        public bool CanRestart { get { return _initSetting.CanRestart; } }
        // 종료시 전환할 base state.
        public string NextState { get { return _initSetting.NextState; } }
        // 종료시 전환할 action category 값.
        public string NextAction { get { return _initSetting.NextAction; } }
        public ReadOnlyCollection<AniPlayData> AniDataList { get; private set; }
        //=== 실행중에 변동하는 값 ===//        
        public virtual bool CanStart { get { return !IsActive && OnCanStart(); } }        
        public bool IsActive { get; protected set; }
        public bool IsCompleted { get; protected set; }        

        //===== rx stream, 이벤트 관련 변수들 ======//        
        protected Subject<ActorAction> _sbjUpdateCanStart;

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================
        protected void Initialize(ActionInitParams setting)
        {
            _initSetting = setting;
            if (!setting.AniDataList.IsNullOrEmpty())
            {
                AniDataList = setting.AniDataList.AsReadOnly();
            }            

            OnInitialize();
        }

        //================================================================================
        // rx stream 메서드 모음 
        //================================================================================
        public IObservable<ActorAction> OnUpdateCanStartAsObservable()
        {
            return _sbjUpdateCanStart ??= this.AddSubject<ActorAction>();
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================ 
        public void Start(Action<ActorAction> cbComplete = null)
        {
            if (!CanStart)
            {
                return;
            }

            IsActive = true;
            IsCompleted = false;
            _cbComplete = cbComplete;

            OnStart();

            _sbjUpdateCanStart?.OnNext(this);
        }

        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            // [210929][jhw] attack 액션에서 공속에 따라 변경된 애니속도를 1로 돌리기 위해 설정.            
            Actor.Look.AniPlayer.Speed = 1;
            OnEnd();

            IsActive = false;
            IsCompleted = false;

            _sbjUpdateCanStart?.OnNext(this);
        }         

        // 액션 동작 완료. End가 호출된 것이 아니므로 아직 Action은 활성화 되어 있는 상태다.
        // 다음 동작에 따라 Action을 전환하고 End가 되거나 기타 다른 행동으로 처리될 수 있다.
        protected void Complete()
        {
            if (!NextState.IsNullOrEmpty())
            {
                Actor.SetState(NextState);
            }
            else if(!NextAction.IsNullOrEmpty())
            {
                Actor.SetNextAction(NextAction);
            }

            OnComplete();

            _cbComplete?.Invoke(this);
            _cbComplete = null;
            IsCompleted = true;

            // 완료시점에도 NextAction이 지정되어 있지 않다면 idle 액션을 지정해서 Action.End가 호출될 수 있도록 한다.
            if (Actor.ActionNextCategory.IsNullOrEmpty())
            {
                Actor.SetNextAction(ActionCategory.idle);
            }
        }     

        protected virtual void OnInitialize() { }
        protected virtual void OnStart() { }
        protected virtual void OnEnd() { }
        protected virtual void OnComplete() { }
        protected virtual bool OnCanStart() { return true; }
    }
}
