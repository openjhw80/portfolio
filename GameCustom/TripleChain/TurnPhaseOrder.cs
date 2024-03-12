using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Eureka;


namespace Eureka
{	
	public enum TurnPhaseOrderType
	{
		None,
        
        Start,
        End,

        //========== unit phase용 ==========//        
        StandBy,        // 입력대기(유저의 터치 입력 or AI의 행동 입력) 상태. 타겟팅 action이 발생하는 order이다.
        // 액티브 스킬.
        PreActive,
		Active,
		PostActive,
		// 일반 공격.
		PreAttack,
		Attack,
		PostAttack,
		// 체인 공격.
		PreChain,
		Chain,		
		PostChain,      //[DJ] 체인에서 파생되는 체인 연관스킬만 넣자.

        DieSkill,       //[DJ] PreActive ~ PostChain 구간에 반응한 행동제약 오브젝트들은 이시점에 작동.
        DropItem,       // 아이템 드랍.
        //========== unit phase용 ==========//

        //========== 몬스터 재배치 phase용 ==========//
        SpawnSpecial,        
        MovementNormal,
        SpawnNormal,
        SpawnMission,
        //========== 몬스터 재배치 phase용 ==========//

        //========== group start phase용 ==========//
        Tick,
        PostTick,   // [DJ] 틱으로 인해 파생되는 스킬동작 (폭탄 틱감소로 폭발 발동같은)
        Support,    // [jhw] blocking처럼 특정 group phase 시작전에 발동해야하는 서포트 action용 order. 만약 세부적인 구별이 필요하면 order를 쪼개야한다.
        //========== group start phase용 ==========//

        //========== turn을 마무리하기 위해 정리하는 phase용 ==========//        
        Return,     // 자기 자리로 돌아가기 혹은 원래상태로 되돌리기.

        //========== 기타 ==========//
        Transforming,       // 변신, 보호막 해제, 상태 전환 등
        PostTransforming,   // 변신 후 처리(transforming 때 actor의 변신 종료 후 처리)
        TileStun,
    }

    public sealed class TurnPhaseOrderTypeComparer : IEqualityComparer<TurnPhaseOrderType>
    {
        public bool Equals(TurnPhaseOrderType x, TurnPhaseOrderType y)
        {
            return (x == y);
        }

        public int GetHashCode(TurnPhaseOrderType type)
        {
            return (int)type;
        }
    }

    // order에 등록할 때 필요한 data 포맷.
    public struct OrderJoiningData
    {
		public TurnPhaseOrderType orderType { get; set; }
		public Actor actor      { get; set; }   // order에 참여하는 actor.
        public Action action    { get; set; }   // order에 실행자로 참여했을 때, 해당 order 실행시에 처리해야할 행동 call back.
        public ActorStateType requiredStates { get; set; }        
        // true면 액션실행 actor, false면 액션실행 actor에 의해 리액션을 실행하는 actor.
        public bool isActionActor { get; set; }        
        // action 혹은 상태종료 체크 call back을 무시함. 일반적으로 외부에서 별도로 종료(End)를 호출하는 경우 true.(ex:StandbyOrder는 전투 시뮬레이터 종료시에 종료됨)
        public bool ignoreAllActionCallBack     { get; set; }
        public bool ignoreChangeStateCallBack   { get; set; }        
        public bool isValid { get { return !(orderType == TurnPhaseOrderType.None || actor == null || (orderType == TurnPhaseOrderType.DieSkill && isActionActor == true && action == null)); } }

        public bool Equals(OrderJoiningData data)
        {
            return (orderType == data.orderType && isActionActor == data.isActionActor && actor.Equals(data.actor));
        }
    }

	/*
	 * phase에서 행동 유닛 리스트 관리 및 순서 제어 클래스.
	 */
	public sealed class TurnPhaseOrder
	{
        // change state 체크에서 무시하고 넘어갈 actor state 타입.
        private static ActorStateType sPassStates = /*ActorStateType.Knockback |*/ ActorStateType.Conversion;

		//====== 멤버변수 ======//		
		private List<Actor>	_actionActorList;   // 해당 order에서 action을 실행하는 actor 리스트. 즉 hitter.
        private List<Actor> _reactionActorList; // 해당 order에서 reaction을 완료해야하는 actor 리스트. target과 hitter 둘다 포함된다.        
        private List<OrderJoiningData> _joiningDataList;
        private Dictionary<Actor, ActorStateType> _requiredStateSet; // actor가 반드시 한 번은 실행해야하는 state type set.

        //====== property ======//
        public TurnPhase phase { get; private set; }
        public TurnPhaseOrderType type { get; private set; }        
        public bool isRunedAllAction { get; private set; }
        public bool isRunning   { get; private set; }   // order가 실행중.      
        public bool endOn { get; private set; }
        // 실행 가능한 order인지 여부. 기본적으로 join된 것이 있을때 true로 켜지지만 직접 true로 설정하고 나중에 join세팅을 할 수 있다.
        public bool isAvailable { get; set; }        

        //====== call back ======//  
        private Action<TurnPhaseOrder, bool> _cbEnd;
		public event Action<TurnPhaseOrder, bool> cbEnd
		{
			add		{ _cbEnd -= value; _cbEnd += value; }
			remove	{ _cbEnd -= value; }
		}

		private TurnPhaseOrder()
		{
            _actionActorList = new List<Actor>();
			_reactionActorList	= new List<Actor>();
            _joiningDataList = new List<OrderJoiningData>();
            _requiredStateSet = new Dictionary<Actor, ActorStateType>();
        }

		public static TurnPhaseOrder FromPhaseWithType(TurnPhase turnPhase, TurnPhaseOrderType type)
		{
			if(turnPhase == null)
			{
				return null;
			}

			var phaseOrder	= new TurnPhaseOrder();
			phaseOrder.phase = turnPhase;
			phaseOrder.type = type;

			return phaseOrder;
		}

		//================================================================================
		// 새로 정의한 일반 메서드 모음./
		//================================================================================
        // order에 참여.
		public void Join(OrderJoiningData data)
		{
			if (!data.isValid)
			{
				return;
			}

            Leave(data);
            _joiningDataList.Add(data);
            isAvailable = true;
        }

        // order에서 빠짐.
        public void Leave(OrderJoiningData data)
        {
            for (int i = 0, count = _joiningDataList.Count; i < count; i++)
            {
                var jData = _joiningDataList[i];
                if (jData.Equals(data))
                {
                    _joiningDataList.RemoveAt(i);
                    break;
                }
            }
        }

        /* [jhoh] 아래 이슈때문에 만듬
         * 1. 드라군 분신 공격으로 무기해제
         * 2. 드라군 본체 체인에 무기 object 타일 공격
         * 3. object 타일에 있던 일반몹이 reactionactor 가 안빠짐
         */
        public void Leave(Actor actor)
        {
            for (int i = 0, count = _joiningDataList.Count; i < count; i++)
            {
                var jData = _joiningDataList[i];
                if (jData.actor.Equals(actor))
                {
                    _joiningDataList.RemoveAt(i);
                    break;
                }
            }
        }

        // joining 데이터는 비활성화 상태에서 등록되기도 하므로 명시적으로 비워야할 경우에만 clear 한다.
        public void ClearJoiningData()
        {
            for (int i = 0, count = _joiningDataList.Count; i < count; i++)
            {                
                ReleaseActorSetting(_joiningDataList[i].actor);
            }
            _joiningDataList.Clear();
        }

        // joining 데이터는 놔두고 나머지 내부 상태 사용 변수들만 clear 한다.
        public void ClearState()
        {
            // [190419][jhw] action 도중 _actionActiorList가 클리어되서 인덱스 exception이 간혹 발생하여 로그 추가.
            if (isRunning && !isRunedAllAction)
            {
                var ownerName = (phase.owner != null) ? phase.owner.name : "null";                
                Debug.LogError(string.Format("OrderRun error #1. orderType:{0}, phase:{1}, owner:{2}"
                    , type, phase.type, ownerName));
            }

            for (int i = 0, count = _reactionActorList.Count; i < count; i++)
            {
                ReleaseActorSetting(_reactionActorList[i]);                
            }

            _reactionActorList.Clear();
            _actionActorList.Clear();
            _requiredStateSet.Clear();
            
            _cbEnd = null;
            isRunedAllAction = false;
            isRunning = false;
            endOn = false;
            isAvailable = false;
        }

		public void Clear()
		{
            ClearJoiningData();            
            ClearState();
        }		

		// order 실행.
		public bool Run(Action<TurnPhaseOrder> cbRuned = null)
		{
            if(isRunning)
            {
                return true;
            }

            Debug.Log(CTag.lightblue(string.Format("TurnPhaseOrder Start. group:{0}, phase:{1}, order:{2}", phase.controller.currentGroupPhase, phase.type, this.type)));

            if (!isAvailable || !Ready())
			{
                Debug.Log(CTag.red(string.Format("TurnPhaseOrder Run Failed. reaction actor count : {0}", _reactionActorList.Count)));

                EndNow(false);                
				return false;
			} 

            isRunning = true;
            for (int i = 0; i < _actionActorList.Count; i++)
            {      
                // [190419][jhw] action 도중 _actionActiorList가 클리어되서 인덱스 exception이 간혹 발생하여 로그 추가.
                if(i >= _actionActorList.Count)
                {
                    var ownerName = (phase.owner != null) ? phase.owner.name : "null";                    
                    Debug.LogError(string.Format("OrderRun error #2. orderType:{0}, i:{1}, count:{2}, phase:{3}, owner:{4}"
                        , type ,i ,_actionActorList.Count, phase.type, ownerName));
                    break;
                }

                _actionActorList[i].RunOrderAction(this);
            }
            isRunedAllAction = true;

            // order 실행에 대한 call back 호출.
            cbRuned?.Invoke(this);

            // RunOrderAction 도중에 이미 reaction처리가 모두 끝나서 종료해야 하는 경우가(ex:붙어있는 폭탄 2개가 터질때)
            // 있으므로 마지막에 체크하여 바로 End처리한다.
            if (CanEnd())
            {
                EndNow();
            }

            return true;
        }

		// order 종료. 외부에서 order를 종료해야 할 때, 호출됨.
		public void End()
		{
            if(!isRunning)
            {
                return;
            }

            endOn = true;
            // run action이 모두 완료되었을 때만 종료 가능하다. 
            // 남아있는 reaction 갯수를 무시하고 무조건 종료하므로 CanEnd와는 조건 체크가 다르다. 
            if (isRunedAllAction)
            {
                EndNow();
            }
        }

        // order 즉시 종료. 내부에서 사용.
        private void EndNow(bool isNormally = true)
        {
            if (!isRunning)
            {
                return;
            }

            Debug.Log(CTag.blue(string.Format("TurnPhaseOrder.EndNow. phase:{0}, order:{1}", phase.type, this.type)));

            endOn = true;
            var cb = _cbEnd;
            cb?.Invoke(this, isNormally);

            Clear();
        }

		private bool CanEnd()
		{
			return endOn || (isRunedAllAction && _reactionActorList.Count <= 0);
		}

        private bool Ready()
        {
            for (int i = 0, count = _joiningDataList.Count; i < count; i++)
            {                
                var data = _joiningDataList[i];
                var actor = data.actor;

                // 메인 행동 actor와 그에 따른 reaction actor 등록.
                if (data.isActionActor && !_actionActorList.Contains(actor))
                {
                    // action 액터도 리액션이 끝나야 하므로 아래에서 같이 체크하여 등록한다.
                    _actionActorList.Add(actor);
                    if(data.action != null)
                    {
                        actor.SetOrderAction(this, data.action);
                    }                    
                }

                // [jhw] 현재 구성은 이미 등록되어 있는 reaction actor를 재등록하지 않는다.
                // 만약 등록되는 count를 별도 저장해서 count 감소를 해야한다면 체크방식을 수정해야하며,
                // count 체크를 하더라도 mainActor의 경우는 한번만 등록해야 하는 경우가 대부분일 것으로 예상되므로 이 부분을 확인하고 수정하기 바람.
                if(_reactionActorList.Contains(actor))
                {
                    continue;
                }
                else
                {
                    _reactionActorList.Add(actor);
                }

                // 행동 종료를 알기위한 call back 등록.
                if(!data.ignoreAllActionCallBack)
                {
                    actor.cbEndOrderAction += OnEndAction;
                }
                if (!data.ignoreAllActionCallBack && !data.ignoreChangeStateCallBack)
                {
                    actor.cbChangedState += OnChangeState;
                }
                
                // 반드시 한 번은 전환해야 하는 state에 대한 설정.
                if(data.requiredStates != ActorStateType.None)
                {
                    if(!_requiredStateSet.ContainsKey(actor))
                    {
                        _requiredStateSet.Add(actor, ActorStateType.None);
                    }
                    _requiredStateSet[actor] |= data.requiredStates;
                }
            }
            _joiningDataList.Clear();

            // 리액션 처리 actor가 한개이상 있어야만 order가 유효하다.
            return (_reactionActorList.Count > 0);
        }

        public void RemoveReactionActor(Actor actor)
        {
            // reaction actor 삭제 및 end 체크.
            if (_reactionActorList.Contains(actor) && BattleSimulator.instance.GetOtherReaction(actor) == false)
            {
                ReleaseActorSetting(actor);
                _reactionActorList.Remove(actor);

                Debug.Log(CTag.olive(string.Format("TurnPhaseOrder RemoveReactionActor. removed reactor. type:{0}, reactor:{1}, Placed:{2}"
                    , type, actor.name, actor.placedTile)));
            }            

            if (CanEnd())
            {
                EndNow();
            }
        }
        
        private void ReleaseActorSetting(Actor actor)
        {
            if(actor == null)
            {
                return;
            }

            actor.cbEndOrderAction  -= OnEndAction;
            actor.cbChangedState    -= OnChangeState;
        }

        //================================================================================
        // 콜백이나 delegate, 이벤트 처리 메서드./
        //================================================================================
        // actor의 명시적 action 종료 알림 call back.
        private void OnEndAction(Actor actor)
        {
            if(actor == null)
            {
                return;
            }

            Debug.Log(CTag.yellow(string.Format("TurnPhaseOrder OnEndAction. type : {0}, actor : {1}", type, actor.name)));

            RemoveReactionActor(actor);

            Debug.Log(CTag.red(string.Format("TurnPhaseOrder RemainActorCheck. type : {0}, count : {1}.", type, _reactionActorList.Count)));
        }

        // actor의 상태 전환에 대한 call back.
        private void OnChangeState(Actor actor, ActorState preState, ActorState nextState, bool isSuccess)
        {
            if(actor == null || preState == null)
            {
                return;
            }

            if (isSuccess)
            {
                Debug.Log(CTag.olive(string.Format("TurnPhaseOrder OnChangeState isSuccess. type:{0}, actor:{1}, stateNow:{2}, stateNext:{3}"
                    , type, actor.name, preState.type, nextState.type)));

                var passed = true;
                var nextStateType = nextState.type;
                if (_requiredStateSet.ContainsKey(actor) && _requiredStateSet[actor] != ActorStateType.None)
                {
                    var requiredState = _requiredStateSet[actor];

                    Debug.Log(CTag.olive(string.Format("TurnPhaseOrder OnChangeState required. type : {0}, actor : {1}, requiredState : {2}"
                        , type, actor.name, requiredState)));
                    
                    if ((requiredState & nextStateType) == 0)
                    {
                        // 필수로 거쳐야 하는 state가 남아 있다면 조건체크를 할 수 없다.
                        passed = false;
                    } 
                    else
                    {
                        requiredState &= ~nextStateType;
                        if(requiredState == ActorStateType.None)
                        {
                            _requiredStateSet.Remove(actor);
                        }
                        else
                        {
                            _requiredStateSet[actor] = requiredState;
                        }                        
                    }
                }
               
                // 조건체크가 가능한 경우. pass state가 아니어야 한다.
                if (passed && ((sPassStates & nextStateType) == 0))
                {
                    // [180814][jhw] actor.isDie 제거. action actor가 자폭이나 폭탄으로 죽는 경우, 이미 hp가 0이어서 isDie에 걸려,
                    // reaction에서 먼저 빠져나가서 다음 order실행에 오류가 발생한다.(ex: Attack(죽음) > Chain(발동안함))
                    if (!actor.IsControl(true) || (nextState.isBaseState || nextStateType == ActorStateType.Die))
                    {
                        RemoveReactionActor(actor);
                    }
                }                
            }
            // state change는 발생했지만 전환에는 실패한 경우.
            else
            {
                Debug.Log(CTag.olive(string.Format("TurnPhaseOrder OnChangeState failed. type:{0}, actor:{1}, stateNow:{2}"
                    , type, actor.name, preState.type)));
                
                // [180814][jhw] actor.isDie 제거. action actor가 자폭이나 폭탄으로 죽는 경우, 이미 hp가 0이어서 isDie에 걸려,
                // reaction에서 먼저 빠져나가서 다음 order실행에 오류가 발생한다.(ex: Attack(죽음) > Chain(발동안함))
                var preStateType = preState.type;                
                if (!actor.IsControl(true))                
                {
                    RemoveReactionActor(actor);
                }
            }
            Debug.Log(CTag.red(string.Format("TurnPhaseOrder RemainActorCheck. type : {0}, count : {1}, changeStatedActor : {2}."
                , type, _reactionActorList.Count, actor.name)));
        }
    }
}