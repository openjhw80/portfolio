using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;


namespace Seeder
{
    // 게임내에서 상호작용하는 모든 개체(유닛, 사물, 환경요소등)의 접근 및 제어(Controller)를 담당하는 클래스.     
    // 상호작용과 관련하여 변화되는 데이터(ex:상태변환)를 관리한다.
    public abstract partial class Actor : ClassBase
    {
        public const string Name = "actor";

        protected enum EPhase { None, Run, OutAction, End }

        //====== action 관련 ======//
        // action pool.  key는 action name.
        protected Dictionary<string, ActorAction> _actionSet = new Dictionary<string, ActorAction>();        
        //====== state 관련 ======//
        // state pool.  key는 state name.
        protected Dictionary<string, ActorState> _stateSet = new Dictionary<string, ActorState>();        
        // 동작중인 sub state 리스트.
        protected List<ActorState> _subStateNowList = new List<ActorState>();
        // 동작을 위해 대기중인 sub state 리스트.
        protected List<ActorState> _subStateAddList = new List<ActorState>();
        // 삭제를 위해 대기중인 sub state 리스트.
        protected List<ActorState> _subStateRemoveList = new List<ActorState>();
        // 대기중인 sub state 리스트를 임시로 합치기 위한 temp 리스트.
        protected List<ActorState> _subStateTempList = new List<ActorState>();        
        protected bool _dirtyState;
        protected bool _dirtyAction;
        protected EPhase _actionPhase = EPhase.None;
        protected ActorState _stateToDeactive;   // Actor가 비활성화 되는 State(die, 복귀등)
        protected ActorState _stateActionNext;   // 다음 action 실행을 위해 선택된 state.

        //=== state        
        public ActorState StateActionNow { get; protected set; } // 현재 실행중인 action이 속한 state.
        public ActorState StateMainNext { get; protected set; }
        public ActorState StateMainNow { get; protected set; }
        public string StateMainNowName { get { return StateMainNow != null ? StateMainNow.Name : string.Empty; } }        
        public bool CanInteraction { get { return IsActive && IsRunAction && _stateToDeactive == null; } }
        //=== action                     
        public ActorAction ActionNow { get; protected set; }
        public string ActionNowCategory { get { return ActionNow != null ? ActionNow.Category : string.Empty; } }
        public string ActionNextCategory { get; protected set; }

        //====== rx stream, 이벤트 관련 변수들 ======//        
        //=== update 관련.
        private Subject<Actor> _sbjEveryUpdate;
        private Subject<Actor> _sbjLateUpdate;
        //=== state or action 전환 관련.
        private Subject<ActorState> _sbjStartState;  // 상태 시작.        
        private Subject<ActorState> _sbjReadyEndState;  // 상태 종료를 위해 out 액션준비중인 상태.
        private Subject<ActorState> _sbjEndState;    // 상태 종료.
        private Subject<Actor> _sbjChangeState;         // 상태 변화.
        private Subject<ActorAction> _sbjStartAction;   // 액션 시작.        
        private Subject<ActorAction> _sbjCompleteAction;// 액션의 모든 동작이 완료된 상태.(종료 및 다음 액션을 위한 사전준비에 사용)
        private Subject<ActorAction> _sbjEndAction;     // 액션 종료.    

        //================================================================================
        // 기초 메서드(생성자, 초기화, destroy, dispose등) 모음
        //================================================================================
        protected Actor() { }

        protected void Initialize(GameObject srcObj)
        {
            // source obj는 local position의 변화가 있을수 있으므로 root object를 별도로 만들어 사용한다.            
            Root = new GameObject().transform;
            Root.position = Values.HidePostion;
            Collider = srcObj.GetComponent<Collider2D>();
           
            srcObj.transform.SetParent(Root);            
            srcObj.transform.localPosition = Vector3.zero;

            Look = Look.FromActor(this);
            // y값이 바뀔때마다 애니메이션 depth 조절.
            Root.ObserveEveryValueChanged(tf => tf.position.y)
                .Subscribe(y => Look.AniPlayer.SortingOrder = -(int)y)
                .AddTo(this);            

            //====== 필요한 스트림 생성 ======//
            // Update stream 생성.
            Observable.EveryUpdate()
                .Where(_ => IsActive && Time.timeScale > 0)
                .Subscribe(_ => EveryUpdate())
                .AddTo(this);
            // LateUpdate stream 생성.
            Observable.EveryLateUpdate()
                .Where(_ => IsActive && Time.timeScale > 0)
                .Subscribe(_ => _sbjLateUpdate?.OnNext(this))
                .AddTo(this);
            //=== 각 state 및 action의 시작과 종료 스트림 등록.
            OnStartStateAsObservable()
                .Subscribe(state => OnStartState(state))
                .AddTo(this);
            OnEndStateAsObservable()
                .Subscribe(state => OnEndState(state))
                .AddTo(this);
            OnStartActionAsObservable()
                .Subscribe(action => OnStartAction(action))
                .AddTo(this);
            OnEndActionAsObservable()
                .Subscribe(action => OnEndAction(action))
                .AddTo(this);

            OnInitialize(); 
        }

        // state 추가 메서드 기본형.
        protected ActorState AddState(Func<StateInitParams, ActorState> funcToCreate,
            StateInitParams initParams)
        {
            if (funcToCreate == null)
            {
                return null;
            }

            var state = funcToCreate(initParams);            
            if (state != null)
            {
                _stateSet.Add(state.Name, state);
            }
            
            return state;
        }

        protected ActorAction AddAction(Func<ActionInitParams, ActorAction> funcToCreate,
            ActionInitParams initParams)
        {
            if (TryGetAction(initParams.Name, out var action))
            {
                return action;
            }
            if (funcToCreate == null)
            {
                return null;
            }

            var actionNew = funcToCreate(initParams);
            TryAddAction(actionNew);

            return actionNew;
        }

        protected bool TryAddAction(ActorAction action)
        {
            if (action == null || _actionSet.ContainsKey(action.Name))
            {
                return false;
            }

            _actionSet.Add(action.Name, action);
            return true;
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            
            Role?.Dispose();
            Look.Dispose();
            foreach (var elem in _actionSet)
            {
                elem.Value.Dispose();
            }
            _actionSet.Clear();
            foreach (var elem in _stateSet)
            {
                elem.Value.Dispose();
            }
            _stateSet.Clear();

            GameObject.Destroy(Root.gameObject);
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================

        //================================================================================
        // rx stream 메서드 모음 
        //================================================================================
        //====== update 관련 ======//
        public IObservable<Actor> OnEveryUpdateAsObservable()
        {
            return _sbjEveryUpdate ??= this.AddSubject<Actor>();
        }
        public IObservable<Actor> OnLateUpdateAsObservable()
        {
            return _sbjLateUpdate ??= this.AddSubject<Actor>();
        }

        //====== state or action 전환 관련 ======//
        public IObservable<ActorState> OnStartStateAsObservable()
        {
            return _sbjStartState ??= this.AddSubject<ActorState>();
        }
        public IObservable<ActorState> OnReadyEndStateAsObservable()
        {
            return _sbjReadyEndState ??= this.AddSubject<ActorState>();
        }
        public IObservable<ActorState> OnEndStateAsObservable()
        {
            return _sbjEndState ??= this.AddSubject<ActorState>();
        }
        public IObservable<Actor> OnChangeStateAsObservable()
        {
            return _sbjChangeState ??= this.AddSubject<Actor>();
        }
        public IObservable<ActorAction> OnStartActionAsObservable()
        {
            return _sbjStartAction ??= this.AddSubject<ActorAction>();
        }
        public IObservable<ActorAction> OnCompleteActionAsObservable()
        {
            return _sbjCompleteAction ??= this.AddSubject<ActorAction>();
        }
        public IObservable<ActorAction> OnEndActionAsObservable()
        {
            return _sbjEndAction ??= this.AddSubject<ActorAction>();
        } 

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================      
        //====== 각 상태와 액션에 따른 정의 ======//
        public void SetActive(bool active)
        {
            if (IsActive == active)
            {
                return;
            }

            // ClearState도중에 콜백으로 중복 호출되는 것을 방지하기 위해 flag를 가장먼저 설정한다.
            IsActive = active;
            Root.gameObject.SetActive(active);
            
            if (!active)
            {                
                _actionPhase = EPhase.None;
                ClearState();
            }

            OnSetActive(active);
        }

        private void ClearState()
        {
            // sub state 종료.
            for (var i = 0; i < _subStateNowList.Count; i++)
            {
                var subState = _subStateNowList[i];                
                subState.EndImmediate();                                
                _sbjEndState?.OnNext(subState);
            }
            // 현재 state 종료.
            if (StateMainNow != null)
            {                
                StateMainNow.EndImmediate();
                _sbjEndState?.OnNext(StateMainNow);
            }
            // 현재 action 종료.
            if (ActionNow != null)
            {
                ActionNow.End();
                _sbjEndAction?.OnNext(ActionNow);
            }

            // [211002][jhw] State나 Action 값은 같은 값을 설정하면 이미 동일한 것이 실행되고 있는 것으로 간주하여 실행되지 않는다.
            // 따라서 Actor를 다시 활성화할 때, State나 Action이 정상적으로 실행될 수 있도록 값을 초기화 한다.
            _stateToDeactive = null;
            _stateActionNext = null;
            StateActionNow = null;
            StateMainNext = null;
            StateMainNow = null;
            ActionNow = null;
            ActionNextCategory = null;            

            _dirtyState = false;
            _dirtyAction = false;            
            _subStateNowList.Clear();
            _subStateAddList.Clear();
            _subStateRemoveList.Clear();
        }

        private void EveryUpdate()
        {
            // 1. end 단계 되었을 때는 다른 과정을 생략하고 모든 액션과 상태를 중지한다.
            // 2. none 단계인데 다음 상태가 설정되지 않았다면 비활성화시킨다.
            //    보통 첫 시작시 활성화 상태에서 state변화가 없을때, 자동 비활성화 처리에 사용된다.
            if (_actionPhase == EPhase.End
            || (_actionPhase == EPhase.None && !_dirtyState))
            {                
                SetActive(false);                
                return;
            }                

            // 실행이 완료된 액션은 미리 중지 한다.
            if (ActionNow != null && ActionNow.IsCompleted)
            {
                EndActionNow();
            }

            //====== state update ======//
            if (_dirtyState)
            {
                _dirtyState = false;
                if (_stateToDeactive != null)
                {
                    // 콜백에서 phase를 참조해야 하므로 미리 설정.
                    _actionPhase = EPhase.OutAction;
                    // 비활성화용 state를 다음 state로 지정한다.
                    _stateActionNext = _stateToDeactive;
                    // start나 end 알림처리 중, subState를 참조 할 수 있으므로 가장 앞에(우선순위가 젤 높으므로) 추가.
                    _subStateNowList.Insert(0, _stateToDeactive);

                    // 현재 now상태를 모두 종료한다. deactvie state만 제외해야 하므로 1부터 시작한다.
                    for (var i = 1; i < _subStateNowList.Count; i++)
                    {
                        var subState = _subStateNowList[i];                        
                        subState.EndImmediate();
                        _sbjEndState?.OnNext(subState);
                    }

                    // Actor 비활성화 상태전환 시작.
                    _stateToDeactive.Start();
                    _sbjStartState?.OnNext(_stateToDeactive);

                    // now list clear 후, 다시 deactive state만 추가.
                    _subStateNowList.Clear();
                    _subStateNowList.Add(_stateToDeactive);                    
                    _stateToDeactive = null;
                }
                else
                {
                    UpdateSubState();
                    UpdateMainState();

                    // End를 호출한 remove state중, actionState로 선별된 것 말고는 전부 완료처리.
                    // main state 업데이트까지 처리해서 actionState 선별이 끝난 이후에 해야 한다.
                    for (var i = 0; i < _subStateRemoveList.Count; i++)
                    {
                        var subState = _subStateRemoveList[i];
                        if (subState != _stateActionNext)
                        {
                            subState.EndImmediate();
                        }
                    }
                    _subStateNowList.Sort(CompareStateLayer);
                }
                
                _subStateAddList.Clear();
                _subStateRemoveList.Clear();
                // 상태의 변화에 대해 알림 처리.
                _sbjChangeState?.OnNext(this);
            }

            //====== action update ======//
            if (_dirtyAction || _stateActionNext != null)
            {
                _dirtyAction = false;
                UpdateAction();

                StateActionNow = _stateActionNext;
                _stateActionNext = null;
                ActionNextCategory = null;                
            }
        }

        //====== state 관련 처리 ======//   
        private int CompareStateLayer(ActorState x, ActorState y)
        {
            if (x.PriorityLayer == y.PriorityLayer)
            {
                return 0;
            }
            else
            {
                return y.PriorityLayer.CompareTo(x.PriorityLayer);
            }
        }
        private void UpdateSubState()
        {
            if (_subStateAddList.Count == 0 && _subStateRemoveList.Count == 0)
            {
                return;
            }
         
            // state call count가 증가하면 실제 종료가 되지 않으므로 add state를 종료보다 먼저 처리한다.
            for (var i = 0; i < _subStateAddList.Count; i++)
            {
                var subState = _subStateAddList[i];
                if (!subState.Start())
                {
                    continue;
                }
                _sbjStartState?.OnNext(subState);

                // now 리스트에 해당 state가 없으면 추가한다.
                if (!_subStateNowList.Contains(subState))
                {
                    _subStateNowList.Add(subState);
                }

                if (!subState.StartAction.IsNullOrEmpty())
                {
                    if (_stateActionNext == null)
                    {
                        _stateActionNext = subState;
                    }
                    else
                    {
                        // 우선순위 값이 action실행 state보다 같거나 높으면 교체
                        var compareValue = _stateActionNext.PriorityLayer.CompareTo(subState.PriorityLayer);
                        if (compareValue >= 0)
                        {
                            _stateActionNext = subState;
                        }
                    }
                }            
            }

            // remove state를 처리한다.
            for (var i = 0; i < _subStateRemoveList.Count; i++)
            {
                var subState = _subStateRemoveList[i];               

                // end에 대한 ready 알림을 보낸다.
                if (subState.ExistOutAction && subState.IsRun)
                {
                    _sbjReadyEndState?.OnNext(subState);
                }                
                subState.End(endAction =>
                {
                    _subStateNowList.Remove(endAction);
                    _sbjEndState?.OnNext(endAction);
                    _sbjChangeState?.OnNext(this);
                });
                
                // out action이 있는 state면 우선순위에 따라 actionState로 지정한다.
                if (subState.ExistOutAction)
                {
                    if (_stateActionNext == null)
                    {
                        _stateActionNext = subState;
                    }
                    else
                    {
                        // remove state는 우선순위 값이 add state와 다르게 높을때만 교체한다.
                        var compareValue = _stateActionNext.PriorityLayer.CompareTo(subState.PriorityLayer);
                        if (compareValue > 0)
                        {
                            _stateActionNext = subState;
                        }
                    }
                }
            }
        }
        private void UpdateMainState()
        {
            if(StateMainNext == null)
            {
                return;
            }

            // stateNow가 end action이 없으면 바로 교체.
            if (StateMainNow == null || !StateMainNow.ExistOutAction || !StateMainNow.IsActive)
            {
                ChangeMainState();
            }
            else
            {
                SetActionStateNow();                

                // actionState가 자기 자신이면 callback 등록. 아니면 바로 ChangeMain.
                if (_stateActionNext == StateMainNow)
                {   
                    _sbjReadyEndState?.OnNext(StateMainNow);
                    StateMainNow.End(_ => _dirtyState = true);                    
                }
                else
                {
                    ChangeMainState();
                }
            }
        }
        private void ChangeMainState()
        {
            // 기존 main state 종료.
            if (StateMainNow != null)
            {
                StateMainNow.EndImmediate();
                _sbjEndState?.OnNext(StateMainNow);
            }                        

            StateMainNow = StateMainNext;
            StateMainNext = null;
            if (StateMainNow == null)
            {
                return;
            }

            // 다음 main state 시작.            
            if(!StateMainNow.Start())
            {
                return;
            }
            if (_actionPhase == EPhase.None)
            {
                _actionPhase = EPhase.Run;                
            }
            _sbjStartState?.OnNext(StateMainNow);

            SetActionStateNow();
        }
        private void SetActionStateNow()
        {
            // action state 지정.
            if (_stateActionNext == null)
            {
                _stateActionNext = StateMainNow;
            }
            else
            {
                // 지정된 actionState가 있을 경우, PoseChange보다 작을 경우에만 main을 actionState로 지정한다. 
                var compareValue = _stateActionNext.PriorityLayer.CompareTo(EStateLayer.PoseChange);
                if (compareValue < 0)
                {
                    _stateActionNext = StateMainNow;
                }
            }
        }
        
        public ActorState GetState(string stateName)
        {
            _stateSet.TryGetValue(stateName, out var state);
            return state;
        }

        public bool RemoveState(string stateName)
        {
            if (!IsActive)
            {
                return false;
            }

            // sub state만 제거할 수 있다.
            var stateLayer = StateName.GetStateLayer(stateName);
            if (stateLayer == EStateLayer.None || stateLayer == EStateLayer.Main)
            {
                return false;
            }
            // state pool이나 subStateNow 리스트에 없으면 제거 불가.
            if (!_stateSet.TryGetValue(stateName, out var subState) || !_subStateNowList.Contains(subState))
            {
                return false;
            }

            if (!_subStateRemoveList.Contains(subState))
            {
                _subStateRemoveList.Add(subState);
                _dirtyState = true;
            }

            return true;
        }

        public bool SetState(string stateName)
        {
            if (!IsActive || string.IsNullOrEmpty(stateName) || _actionPhase > EPhase.OutAction)
            {
                return false;
            }

            // 종료액션 중에는 none 상태만 설정하고 나머지는 설정하지 않는다.
            if (_actionPhase == EPhase.OutAction)
            {
                if (stateName == StateName.none)
                {
                    _actionPhase = EPhase.End;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var stateLayer = StateName.GetStateLayer(stateName);
            if (stateLayer == EStateLayer.None
            || !_stateSet.TryGetValue(stateName, out var state))
            {

                // 관련된 상태가 없다면 변환 불가.
                return false;
            }

            // 비활성화가 되는 state는 sub에 속하지만 별도 설정 한다.
            if (state.IsToDeactive)
            {
                // run 상태가 아니면 그냥 리턴한다.
                if (!IsRunAction)
                {
                    return false;
                }
                
                // 비활성화를 위한 state가 없거나 새로운 state의 우선순위가 높으면 교체.
                if (_stateToDeactive == null || _stateToDeactive.PriorityLayer < state.PriorityLayer)
                {
                    _stateToDeactive = state;
                }
                // sub의 main의 신규 state는 모두 clear 한다.
                StateMainNext = null;
                _subStateAddList.Clear();
                _dirtyState = true;

                return true;
            }
            else
            {
                switch (stateLayer)
                {
                    case EStateLayer.Main:
                        return SetMainState(state);
                    default:
                        return AddSubState(state);
                }
            }
        }

        // 다음 main state 설정.
        protected bool SetMainState(ActorState nextState)
        {
            if (nextState == null)            
            {
                return false;
            }

            if (nextState != StateMainNow)
            {
                StateMainNext = nextState;
                _dirtyState = true;
            }

            return true;
        }

        // 추가 state 적용
        protected bool AddSubState(ActorState subState)
        {
            if (subState == null || _subStateAddList.Contains(subState))            
            {
                return false;
            }

            _subStateAddList.Add(subState);
            _dirtyState = true;

            return true;
        }

        // 현재 state를 고려하여 액션을 발동할 수 있는 유효한 상태를 가져온다. 인자로 ActionCategory를 사용한다.
        public bool TryGetValidState(string actionCategory, out ActorState state)
        {
            state = null;
            if (actionCategory.IsNullOrEmpty())
            {
                return false;
            }

            for (int i = 0; i < _subStateNowList.Count; i++)
            {                
                var subState = _subStateNowList[i];
                if (!subState.CanAction(actionCategory))
                {                    
                    // black list에 걸리면 action 실행 불가능.
                    return false;
                }

                // white list에 있는 액션인 상태에서 실제 action name이 있으면 설정.
                if (subState.TryGetAction(actionCategory, out _))
                {
                    state = subState;
                    return true;
                }
            }

            // sub에 없으면 main에서 동일하게 체크.
            if (StateMainNow.CanAction(actionCategory) && StateMainNow.TryGetAction(actionCategory, out _))
            {
                state = StateMainNow;
                return true;
            }            

            return false;
        }

        public bool ExistStateNow(string stateName)
        {
            for (int i = 0; i < _subStateNowList.Count; i++)
            {                
                if (_subStateNowList[i].Name == stateName)
                {
                    return true;
                }                
            }

            return StateMainNow != null && StateMainNow.Name == stateName;
        }

        //====== action 관련 처리 ======//
        // 현재 지정된 now 액션 종료.
        private void UpdateAction()
        {
            // state start나 end로 action state가 지정되어 있는 경우. ActionNextCategory를 지정한다.
            // ActionNextCategory를 지정 후, 실제로 해당 액션을 실행할 유효 state를 다시 가져와야 한다.
            if (_stateActionNext != null)
            {
                var actionCat = _stateActionNext.IsRun ? _stateActionNext.StartAction : ActionCategory.@out;
                if (!actionCat.IsNullOrEmpty())
                {
                    ActionNextCategory = actionCat;
                }
                else if(ActionNextCategory.IsNullOrEmpty())
                {
                    // 지정된 다음 action이 없으면 아무 동작도 하지 않는다.
                    return;
                }

                // 지정된 action이 실행가능한지 유효 state를 가져옴. 실행 불가능하면 그냥 종료.
                if (!TryGetValidState(ActionNextCategory, out var stateValid))
                {
                    return;
                }
                // 유효 state가 stateActionNext보다 우선순위가 높으면 교체.
                if (_stateActionNext.PriorityLayer < stateValid.PriorityLayer)
                {
                    _stateActionNext = stateValid;
                }
            }
            // 지정된 action state가 없는 경우.(state변화가 없이 action만 변경)
            else
            {
                if (!TryGetValidState(ActionNextCategory, out _stateActionNext))
                {
                    // 유효한 상태가 없는 경우, idle로 지정한다.
                    ActionNextCategory = ActionCategory.idle;
                    if (!TryGetValidState(ActionNextCategory, out _stateActionNext))
                    {
                        // idle도 없다면(그럴리는 없지만...) 그냥 종료.
                        return;
                    }
                }
            }

            _stateActionNext.TryGetAction(ActionNextCategory, out var nextAction);
            ChangeAction(nextAction);
        }

        private void ChangeAction(ActorAction nextAction)
        {
            // now와 next가 같은 액션인데 재실행을 지원하지 않는 경우에는 그냥 리턴.
            // ex) 스턴 액션중에 다시 스턴상태가 된 경우, 중복된 액션 재실행 없이 기존 액션 유지.
            if (ActionNow != null && ActionNow == nextAction && !ActionNow.CanRestart)
            {
                return;
            }

            // 현재 액션 종료.
            EndActionNow();

            // 다음 action 등록 및 실행.
            ActionNow = nextAction;
            if(ActionNow != null)
            {                
                ActionNow.Start(action => _sbjCompleteAction?.OnNext(action));
                _sbjStartAction?.OnNext(ActionNow);       
            }
        }
        // 현재 액션 종료 처리.
        private void EndActionNow()
        {
            if (ActionNow == null)
            {
                return;
            }
            
            ActionNow.End();
            _sbjEndAction?.OnNext(ActionNow);            
            ActionNow = null;
        }
        
        // 현재 state를 고려하여 발동가능성 있는 유효한 액션을 가져온다. 인자로 ActionCategory를 사용한다.
        public bool TryGetValidAction(string actionCategory, out ActorAction action)
        {
            action = null;
            if (!TryGetValidState(actionCategory, out var validState))
            {
                return false;
            }

            return validState.TryGetAction(actionCategory, out action);
        }
        public bool TryGetAction(string actionName, out ActorAction action)
        {
            action = null;
            return !actionName.IsNullOrEmpty() && _actionSet.TryGetValue(actionName, out action);
        }

        public void SetBaseAction()
        {
            if (ActionNowCategory == ActionCategory.idle)
            {
                return;
            }

            SetNextAction(ActionCategory.idle);
        }
        public void SetNextAction(string actionCategory)
        {
            if (string.IsNullOrEmpty(actionCategory))            
            {
                return;
            }

            ActionNextCategory = actionCategory;
            _dirtyAction = true;
        }

        protected virtual void OnInitialize() { }
        protected virtual void OnStartState(ActorState state) { }
        protected virtual void OnEndState(ActorState state) { }
        protected virtual void OnStartAction(ActorAction action) { }
        protected virtual void OnEndAction(ActorAction action) { }
        protected virtual void OnSetActive(bool active) { }

        //================================================================================
        // 이벤트 및 콜백 처리
        //================================================================================  
    }
}
