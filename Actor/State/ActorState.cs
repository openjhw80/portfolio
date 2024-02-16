using System;
using System.Collections.Generic;
using UniRx;


namespace Seeder
{
    public abstract class ActorState : ClassBase
    {
        protected enum EPhase { Run, ReadyOutAction, OutAction, End }

        // white list에 등록된 것은 black list에서 예외가 되어 실행 가능한 액션 이름을 반환한다.
        // key는 action category, value는 action name.
        protected Dictionary<string, string> _actionWhiteList = new Dictionary<string, string>();
        // black list에 등록된 action은 실행 불가능하다. 단 white list에 등록된 것은 예외이다.
        // 모든 action을 금지할 경우에는 ActionCategory.all을 등록한다.
        protected HashSet<string> _actionBlackList = new HashSet<string>();
        protected StateInitParams _initSetting;
        protected EPhase _phase = EPhase.End;        
        protected bool _completeStartAction;    // 시작 액션 완료 여부.
        protected Action<ActorState> _cbEndComplete;        

        //=== 변동없는 고정값 ===//        
        public Actor Actor { get { return _initSetting.Actor; } }
        public string Name { get { return _initSetting.Name; } }
        // 상태 시작시, 시작할 action category.
        public string StartAction { get { return ExistInAction ? ActionCategory.@in : _initSetting.StartAction; } }        
        public EStateLayer PriorityLayer { get { return StateName.GetStateLayer(Name); } }
        public bool IsMain { get { return PriorityLayer == EStateLayer.Main; } }
        public bool ExistInAction { get { return _actionWhiteList.ContainsKey(ActionCategory.@in); } }
        public bool ExistOutAction { get { return _actionWhiteList.ContainsKey(ActionCategory.@out); } }              
        //=== 실행중에 변동하는 값 ===//        
        public bool IsActive { get { return _phase != EPhase.End; } }        
        public bool IsRun { get { return _phase == EPhase.Run; } }
        public bool IsReadyOutAction { get { return _phase == EPhase.ReadyOutAction; } }
        public bool IsOnOutAction { get { return _phase == EPhase.OutAction; } }        
        public bool IsToDeactive { get; protected set; }    // Actor를 비활성화 시키는 상태인 경우(die, 복귀등)

        //====== rx stream, 이벤트 관련 변수들 ======//


        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================          
        protected void Initialize(StateInitParams setting)
        {
            _initSetting = setting;            

            // action 시작에 대한 스트림 구독.
            Actor.OnStartActionAsObservable()
                .Where(_ => IsActive)
                .Subscribe(action => ActionStart(action))
                .AddTo(this);
            // action 완료에 대한 스트림 구독.
            Actor.OnEndActionAsObservable()
                .Where(_ => IsActive)
                .Subscribe(action => ActionEnd(action))
                .AddTo(this);

            OnInitialize();
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        public bool Start()
        {
            if (IsRun)
            {
                return false;
            }

            // end action도중에 다시 start가 될 경우, 콜백을 호출하지 않도록 null로 설정한다.
            _cbEndComplete = null;
            _completeStartAction = false;
            _phase = EPhase.Run;            

            OnStart();

            return true;
        }

        public void End(Action<ActorState> cbComplete = null)
        {
            // Run상태가 아니면 End가 될 수 없다.
            if (!IsRun)
            {
                return;
            }

            _cbEndComplete = cbComplete;
            // end action이 있고 현재 state가 액션으로 선택된 경우, 액션 이후에 다시 End를 호출하도록 한다.
            if (ExistOutAction)
            {
                _phase = EPhase.ReadyOutAction;                
                return;
            }
            else
            {
                SetEndPhase();
            }
        }

        // 모든 것을 무시하고 즉시 종료 처리.
        public void EndImmediate()
        {
            if (!IsActive)
            {
                return;
            }

            SetEndPhase();
        }

        // 해당 카테고리의 액션이 현재 상태에서 가능한지 체크.
        public bool CanAction(string actionCategory)
        {
            if (actionCategory.IsNullOrEmpty())
            {
                return false;
            }

            // white list에 있으면 무조건 가능.
            if (_actionWhiteList.ContainsKey(actionCategory))
            {
                return true;
            }
            else
            {
                // black list에 카테고리가 있거나 all이 등록되어 있으면 실행 불가능.
                if (_actionBlackList.Contains(actionCategory) || _actionBlackList.Contains(ActionCategory.all))
                {
                    return false;
                }
                // white list에도 없고 black list에도 없으면 액션 가능으로 취급되며 하위 state로 해당 액션 제어를 체크해야 한다.
                else
                {
                    return true;
                }
            }
        }

        // 해당 ActionCategory에 대한 실제 액션을 가져오는 메서드.
        // 액션이 없다고 해서 액션 자체가 block 되어 있다는 것은 아니다. 액션 실행 가능여부는 CanAction으로 체크해야 한다.
        public bool TryGetAction(string actionCategory, out ActorAction action)
        {
            action = null;
            if (actionCategory.IsNullOrEmpty())
            {
                return false;
            }

            if (!_actionWhiteList.TryGetValue(actionCategory, out var actionName))
            {
                return false;
            }

            return Actor.TryGetAction(actionName, out action);
        }

        //====== 화이트리스트 액션 관련 ======//
        public void AddToWhiteList(ActorAction action)
        {
            if (action != null)
            {
                AddToWhiteList(action.Category, action.Name);                
            }
        }
        public void AddToWhiteList(string actionCategory, string actionName = "")
        {
            if (actionCategory.IsNullOrEmpty())
            {
                return;
            }

            _actionWhiteList[actionCategory] = actionName;

            if (Actor.TryGetAction(actionName, out var action) && action.NextState == StateName.none)
            {
                IsToDeactive = true;
            }
        }
        //====== 블랙리스트 액션 관련 ======//
        public void AddToBlackList(string actionCategory)
        {
            if (actionCategory.IsNullOrEmpty())
            {
                return;
            }

            _actionBlackList.Add(actionCategory);
        }

        // 특정 action이 시작 되었을 때.
        protected void ActionStart(ActorAction action)
        {
            switch (_phase)
            {
                case EPhase.ReadyOutAction:
                    // 시작된 action이 end action하고 같으면 지정된 end action이 시작된 것이므로 phase 변경.
                    if (_actionWhiteList.ContainsValue(action.Name))                        
                    {
                        _phase = EPhase.OutAction;
                    }
                    else
                    {
                        SetEndPhase();
                    }
                    break;                
            }
            OnActionStart(action);

            // 시작 액션이 완료되지 않았으면 시작 액션 여부 체크
            if(!_completeStartAction)
            {
                // 지정된 start action이 아닌 경우에는 우선순위에 따라 다른 액션으로 대체된 것이므로 바로 완료를 호출한다.
                if (action.Category != StartAction)
                {
                    OnCompleteStartAction(action);
                    _completeStartAction = true;
                }
            }
        }
        // 특정 action이 종료 되었을 때.
        protected void ActionEnd(ActorAction action)
        {
            // 화이트 리스트에 변환 카테고리 이름이 있으면 해당 state의 action이 실행 됨.
            switch (_phase)
            {   
                case EPhase.OutAction:
                    if (_actionWhiteList.ContainsValue(action.Name)) SetEndPhase();                              
                    break;
            }
            OnActionEnd(action);

            // 시작 액션이 완료되지 않았으면 완료 처리.
            if (!_completeStartAction)
            {
                OnCompleteStartAction(action);
                _completeStartAction = true;
            }
        }       
        protected virtual void SetEndPhase()
        {
            // Actor를 종료시키는 state에 의해 강제 종료되었을 경우에 true.
            OnEnd(Actor.IsOutAction);
            _phase = EPhase.End;

            _cbEndComplete?.Invoke(this);
            _cbEndComplete = null;
        }

        protected virtual void OnInitialize() { }
        protected virtual void OnStart() { }
        protected virtual void OnEnd(bool byForcedStop) { }
        protected virtual void OnCompleteStartAction(ActorAction action) { }
        protected virtual void OnActionStart(ActorAction action) { }
        protected virtual void OnActionEnd(ActorAction action) { }        
    }
}
