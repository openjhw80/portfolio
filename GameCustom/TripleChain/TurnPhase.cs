using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Eureka;


namespace Eureka
{
    public enum TurnPhaseType
    {
        None,

        Unit,
        UnitReady,
        Relocation,
        GroupStart,
        GroupEnd,
        SyncPhase,
        DropPhase,
        TileStun,
        End,
    }
    
    public struct TurnPhaseData
    {
        public TurnPhaseType type { get; set; }        
        public UnitPhaseGroup group { get; set; } 
        public int index { get; set; }      // group list 내부의 위치 index.
        public Actor owner { get; set; }    // 행동유닛 actor 
    }

    /*
	 * 턴 Phase 처리 base 클래스.
	 */
    public abstract class TurnPhase
	{
        //====== 멤버변수 ======//                
        protected Dictionary<int, TurnPhaseOrder> _orderPool;   // key는 TurnPhaseOrderType.                
        protected List<TurnPhaseOrder> _currentOrderList;   // 현재 동작할 order list.        
        public List<TurnPhaseOrder> currentOrderList { get { return _currentOrderList; } }   // 현재 동작할 order list.        

        //====== property ======//       
        public TurnController controller { get; protected set; }
        public Actor owner { get; protected set; }
        // owner가 될 수 있는 후보. 죽었거나 스턴이거나 상관없이 정상적인 상황이라고 가정할 경우, owner가 될 수 있으면 추가함.
        // 왜냐하면 스턴이나 죽었더라도 다른 유닛에 의해 상태이상이 해제되거나 부활하여 owner로 처리될 수 있기 때문이다.
        // 동적으로 owner 처리가 가능한지 여부는 CanUseToOwner() 메서드 내부에서 하도록 한다.(UnitPhase에 있음)
        public List<Actor> candidateList { get; protected set; }
        public TurnPhaseType type { get; protected set; }    
        public TurnPhaseOrder currentOrder { get; protected set; }
        public int currentOrderIndex { get; protected set; }
        public bool isRunning { get; protected set; }
        public bool isOrderEnded { get; protected set; }    
        public bool ignoreAutoActionSetting { get; set; }
        public bool checkSimulator { get; set; }   // next order를 넘어가기 전에 시뮬레이터 체크를 해서 reaction actor를 등록할 경우.
        // 상태업데이트 체크를 위한 flag.
        public bool isDirty { get { return isOrderEnded; } }

        //====== call back ======//        
        // turn phase 종료 직전 호출.
        protected Action<TurnPhase, bool> _cbEnd;
        public event Action<TurnPhase, bool> cbEnd
        {
            add     { _cbEnd -= value; _cbEnd += value; }
            remove  { _cbEnd -= value; }
        }

        protected TurnPhase()
        {
            _orderPool = new Dictionary<int, TurnPhaseOrder>();
            _currentOrderList = new List<TurnPhaseOrder>();
            candidateList = new List<Actor>();
        }

        public static TurnPhase FromControllerWithType(TurnController controller, TurnPhaseType type)
        {
            if (controller == null)
            {
                return null;
            }

            TurnPhase phase = null;
            switch(type)
            {
                case TurnPhaseType.Unit:
                    phase = UnitPhase.FromController(controller);
                    break;
                case TurnPhaseType.UnitReady:
                    phase = UnitReadyPhase.FromController(controller);
                    break;
                case TurnPhaseType.Relocation:
                    phase = RelocationPhase.FromController(controller);
                    break;
                case TurnPhaseType.GroupStart:
                    phase = GroupStartPhase.FromController(controller);
                    break;
                case TurnPhaseType.GroupEnd:
                    phase = GroupEndPhase.FromController(controller);
                    break;
                case TurnPhaseType.SyncPhase:
                    phase = SyncPhase.FromController(controller);
                    break;
                case TurnPhaseType.DropPhase:
                    phase = DropPhase.FromController(controller);
                    break;
                case TurnPhaseType.TileStun:
                    phase = TileStunPhase.FromController(controller);
                    break;
                case TurnPhaseType.End:
                    phase = EndPhase.FromController(controller);
                    break;
            }           

            return phase;
        }

        //================================================================================
        // 상속 혹은 interface의 메서드 모음./
        //================================================================================

        //================================================================================
        // 새로 정의한 일반 메서드 모음./
        //================================================================================  
        public void UpdatePhase()
        {            
            if(!isRunning || controller.isPause)            
            {
                return;
            }

            // order가 종료된 상태면 다음 order를 가져와서 실행한다.
            if(isOrderEnded)
            {
                RunNextOrder();
            }
        }

        public TurnPhaseData GetPhaseData()
        {
            var group = UnitPhaseGroup.None;
            var index = -1;
            controller.GetPosition(this, out group, out index);

            var data = new TurnPhaseData();
            data.type = this.type;
            data.owner = this.owner;
            data.group = group;
            data.index = index;

            return data;
        }

        public TurnPhase GetPrePhase()
        {
            var data = GetPhaseData();
            return controller.GetTurnPhase(data.group, data.index-1);
        }

        public TurnPhase GetNextPhase()
        {
            var data = GetPhaseData();
            return controller.GetTurnPhase(data.group, data.index+1);
        }

        public void Join(OrderJoiningData data)
        {
            if (!data.isValid)
            {
                return;
            }

            var order = GetOrder(data.orderType);
            if (order != null)
            {
                order.Join(data);
            }
        }

        public TurnPhaseOrder GetOrder(TurnPhaseOrderType orderType)
        {
            var key = (int)orderType;
            if(!_orderPool.ContainsKey(key))
            {
                Debug.LogError(string.Format("등록되어 있지 않은 TurnOrder임. phase:{0}, order:{1}"
                    , this, orderType));

                return null;
            }

            return _orderPool[key];
        }

        protected void JoinDropItems()
        {
            var dropItemSet = BattleSimulator.instance.dropItemSet;
            var enumer = dropItemSet.GetEnumerator();
            while(enumer.MoveNext())
            {
                var item = enumer.Current.Value;
                if(item == null)
                {
                    continue;
                }

                var joiningData = new OrderJoiningData();
                joiningData.actor = item;
                joiningData.orderType = TurnPhaseOrderType.DropItem;
                //joiningData.requiredStates = ActorStateType.Appear;
                Join(joiningData);
            }
        }

        protected void RunDropItems()
        {            
            var dropItemSet = BattleSimulator.instance.dropItemSet;
            var copyDropItemSet = new Dictionary<int, Actor>(dropItemSet);
            foreach(var elem in copyDropItemSet)
            {
                var index = elem.Key;
                var item = elem.Value;
                var tile = controller.director.map.totalTiles[index];
                if (tile == null || item == null)
                {
                    continue;
                }

                item.SetActive(true);

                if (item.role.appearBehavior != null)
                {
                    var appearData = new AppearData();
                    appearData.owner = item.owner;
                    appearData.isDropItem = item.isDropItem;
                    appearData.startTile = tile;
                    appearData.endTile = tile;

                    item.TransitionState(ActorStateType.Appear, appearData);
                }
                else
                {
                    item.SetTile(tile);
                    item.Idle();
                }
            }

            dropItemSet.Clear();
            copyDropItemSet.Clear();
        }

        // phase에서 사용하는 order를 pool에 등록.
        protected void AddToOrderPool(TurnPhaseOrderType type)
        {            
            _orderPool.Add((int)type, TurnPhaseOrder.FromPhaseWithType(this, type));
        }

        // pool에 있는 order를 진행할 order list에 추가.
        // isAvailable은 join 된 것이 없더라도 next order에서 가져올 수 있도록 할 때, 설정한다.
        protected void AddToCurrentOrderList(TurnPhaseOrderType orderType, bool isAvailable = false)
        {
            var order = GetOrder(orderType);
            order.isAvailable = isAvailable;

            _currentOrderList.Add(order);
        }

        // [jhw] order를 clear할 때, 일반적인 Clear를 하면 내부에 쌓아둔 joiningData도 clear 된다.
        // 따라서 joiningData를 남겨둘지 여부를 체크하여 clear 처리를 해야한다.
        protected void ClearOrders(bool includeJoiningData)
        {
            var enumer = _orderPool.GetEnumerator();
            while(enumer.MoveNext())
            {
                var order = enumer.Current.Value;
                if(order != null)
                {
                    order.Clear();

                    // stand by는 행동하는 unit 전용이고 ready 시점에 재등록하므로 clear 한다.
                    if (includeJoiningData || order.type == TurnPhaseOrderType.StandBy)
                    {
                        order.Clear();
                    }
                    else
                    {
                        order.ClearState();
                    }
                }
            }
            
            _currentOrderList.Clear();
            currentOrderIndex = -1;
            currentOrder = null;
        }

        public void Clear()
        {
            OnClearing(true);

            ClearOrders(true);            
            _cbEnd = null;
            owner = null;
            candidateList.Clear();
            
            isRunning = false;
            isOrderEnded = false;
            ignoreAutoActionSetting = false;
            checkSimulator = false;

            OnClearing(false);
        }

        public bool Run()
        {
            if (isRunning)
            {
                return true;
            }

            Debug.Log(CTag.grey(string.Format("TurnPhase Start. group:{0}, phase:{1}"
                , controller.currentGroupPhase, type)));

            if (!CanRun())
            {
                EventMessenger.Send(EventName.turnPhaseCantRun, this);
                End(false);
                return false;
            }

            isRunning = true;
            ClearOrders(false);

            Ready();
            RunNextOrder();

            return true;
        }

        protected void RunNextOrder()
        {
            isOrderEnded = false;            
            while (true)
            {
                var preOrder = currentOrder;
                var nextOrder = GetNextOrder();
                if (nextOrder != null)
                {
                    OnChangeNextOrder(preOrder, nextOrder);

                    if(checkSimulator)
                    {
                        BattleSimulator.instance.ClearPhaseResult();
                        BattleSimulator.instance.cbCompleteAllSimulation += OnCheckSimulator;
                        break;
                    }
                    else
                    {
                        // [jhw] nextOrder가 실행을 실패하면 update를 통하지 않고 while문에 의해 곧바로 다음 next order를 얻어와서 실행하도록 수정.
                        nextOrder.cbEnd += OnEndOrder;
                        if (nextOrder.Run(OnRunedOrder))
                        {                            
                            break;
                        }
                    }
                }
                else
                {
                    OnCompleteAllOrder(preOrder);
                    End();
                    break;
                }
            }            
        }

        protected TurnPhaseOrder GetNextOrder()
        {
            while (true)
            {
                currentOrderIndex++;
                if (currentOrderIndex < 0 || currentOrderIndex >= _currentOrderList.Count)
                {
                    currentOrder = null;
                    return null;
                }

                var order = _currentOrderList[currentOrderIndex];
                if (order.isAvailable)
                {
                    currentOrder = order;
                    return order;
                }
                else
                {
                    order.Clear();
                }
            }
        }

        protected void JoinActors(TurnPhaseOrderType orderType, Actor mainActor, List<Actor> reactionActorList)
        {
            if (mainActor != null)
            {
                // main actor order에 추가.
                var mainJoiningData = new OrderJoiningData();
                mainJoiningData.actor = mainActor;
                mainJoiningData.isActionActor = true;
                mainJoiningData.orderType = orderType;
                Join(mainJoiningData);
            }

            // reaction actor order 추가.
            for (int i = 0, count = reactionActorList.Count; i < count; i++)
            {
                var reactionActor = reactionActorList[i];
                var reactionJoiningData = new OrderJoiningData();
                reactionJoiningData.actor = reactionActor;
                reactionJoiningData.orderType = orderType;
                Join(reactionJoiningData);
            }
        }    

        protected void End(bool isNormally = true)
        {
            if(!CanEnd())
            {
                return;
            }

            OnEndPhase();

            var cb = _cbEnd;
            cb?.Invoke(this, isNormally);
            
            Clear();
        }

        public virtual void Reset() { }
        protected virtual bool CanRun() { return true; }
        protected virtual bool CanEnd() { return true; }
        protected virtual void Ready() { }
        //============ 메서드 실행중, 특정 시점에 대한 처리 가상함수 모음 ===============//
        // isFirst가 true면 첫시작, false면 마지막에 호출된 것임.
        protected virtual void OnClearing(bool isFirst) { }
        // order의 action actor들의 행동이 실행되고 나서 호출되는 call back. run이 실패면 호출하지 않음.
        protected virtual void OnRunedOrder(TurnPhaseOrder order) { }
        // next order를 run하기 직전에 호출되는 메서드.
        protected virtual void OnChangeNextOrder(TurnPhaseOrder preOrder, TurnPhaseOrder nextOrder) { }        
        // order 하나가 종료될 때마다 호출되는 메서드.
        protected virtual void OnEndingOrder(TurnPhaseOrder order) { }
        // 모든 order가 종료되고 phase end 실행전에 호출되는 메서드.
        protected virtual void OnCompleteAllOrder(TurnPhaseOrder lastOrder) { }
        // 페이즈End할때의 콜백.
        protected virtual void OnEndPhase()
        {
            Debug.Log(CTag.red(string.Format("[OnEndPhase Call] owner:{0}, group:{1}, phaseType:{2}"
                , owner, controller.currentGroupPhase, type)));
        }
        //============ 메서드 실행중, 특정 시점에 대한 처리 가상함수 모음 ===============//
        //================================================================================
        // 콜백이나 delegate, 이벤트 처리 메서드./
        //================================================================================        
        // order의 end가 실행되고 바로 호출되는 call back.
        protected virtual void OnEndOrder(TurnPhaseOrder order, bool isEndedNormally)
        {
            OnEndingOrder(order);

            isOrderEnded = isEndedNormally;
        }

        // 특정 order 실행전에 시뮬레이터를 체크하여 reaction actor를 등록할 경우.        
        private void OnCheckSimulator(BattleSimulator bs)
        {
            bs.cbCompleteAllSimulation -= OnCheckSimulator;            

            var resultList = new List<SimulateResult>();
            for (int i = 0, oCount = _currentOrderList.Count; i < oCount; i++)
            {
                var orderType = _currentOrderList[i].type;
                resultList.Clear();
                if (!bs.GetPhaseResult(orderType, ref resultList))
                {
                    continue;
                }

                for (int k = 0, rCount = resultList.Count; k < rCount; k++)
                {
                    var simulationResult = resultList[k];
                    // action actor는 동적으로 등록하는 각 스킬에서 미리 등록하므로 할 필요 없음.
                    JoinActors(orderType, null, simulationResult.reactionActors);
                }                
            }

            checkSimulator = false;            
            currentOrder.cbEnd += OnEndOrder;
            if (!currentOrder.Run(OnRunedOrder))
            {
                RunNextOrder();
            }            
        }
    }
}