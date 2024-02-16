using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Eureka;


namespace Eureka
{
    // [jhw] UnitPhaseGroup은 Phase 실행 순서를 묶음단위로 나타내는 것이며 phaseGroup 소속이 그 유닛의 role group을 의미하는 것은 아니다.
    // 예를 들어 특정 몬스터가 용병들이 움직이기 전에 선행동을 하는 경우라면 role group은 몬스터이지만 UnitPhaseGroup은 Mercenary에 속해서 실행 된다.
    [Flags]
    public enum UnitPhaseGroup
    {
        None    = 0,
        
        Ready       = 1 << 0,
        Charm       = 1 << 1,
        MercenaryReady = 1 << 2,
        NPC         = 1 << 3,
        Mercenary   = 1 << 4,
        Tile        = 1 << 5,
        Champion    = 1 << 6,
        Monster     = 1 << 7,
        EndTurn     = 1 << 8,

        SetMercenary = Charm | MercenaryReady | Mercenary,
        SetMonster = Champion | Monster,
        All = int.MaxValue
    }

    // [181217][jhw] 서버의 보너스턴 flag와 매칭되어 있으므로 맘대로 순서를 바꿔서는 안된다.(network api의 R_PVP_TURN_END 참조)
    public enum BonusTurn
    {
        None,

        Skill,      // 스킬 보너스턴.            
        Chain,      // 체인 보너스턴.              
        Addition,   // 특정기능(스킬,버프,시스템등)에 의해 부여받은 별도의 추가턴.

        // [190508][jhw] 멀티플레이에서 턴 소유자가 자신의 턴을 이관하는 경우에 사용.(Disconnect, Giveup등)
        // 엄밀히 말해 개인보너스는 아니고 turn감소를 시키지 않기 위한 팀의 보너스턴 개념이다.
        // turncontroller에서 사용하지는 않지만 서버통신시, bonus flag에 4번으로 전달하기 때문에 삭제하거나 순서를 변경하면 안된다.
        Pass,       
    }

    /*
	 * 턴 제어 관리 클래스.
	 */
    public sealed class TurnController
    {
        //================ 멤버 변수 ================//        
        private List<TurnPhase> _currentPhaseList;
        private Dictionary<int, List<TurnPhase>> _groupPhaseListSet;   // key는 UnitPhaseGroup의  int값.
        private List<UnitPhaseGroup> _groupOrder;
        private Dictionary<int, List<TurnPhase>> _turnPhasePool;    // key는 TurnPhaseType의 int값.        
        private int _bonusPhaseIndex;

        //================ property ================//        
        public BattleBaseDirector director { get; private set; }
        public List<Actor> championOrderList { get; private set; }
        public List<int> actorSeqList { get; private set; }
        public UnitPhase mainUnitPhase { get; private set; }    // 해당턴을 주도하는 메인용병(나 혹은 다른 유저의 용병이 될 수 있음)      
        public TurnPhase currentPhase { get; private set; }
        public UnitPhaseGroup currentGroupPhase { get; private set; }
        public int currentGroupIndex { get; private set; }
        public int currentPhaseIndex { get; private set; }
        public bool isPhaseEnded { get; private set; }
        public bool isRunning { get; private set; }
        public bool isPause { get; private set; }
        public bool isEventScene { get { return _isEventScene; } }
        public bool onInput { get; private set; }   // 터치 입력 대기 가능 상태 체크.
        public BonusTurn bonusTurn { get; private set; }
        public bool isBonusTurn { get { return bonusTurn != BonusTurn.None; } }
        // [181217][jhw] 일단 호환성을 위해 남겨둔다. 나중에 bonusTurn으로 통합하고 삭제 처리할 예정.
        public bool isChainBonusPhase { get { return (_bonusPhaseIndex >= 0); } }
        public bool isSkillBonusPhase { get; set; }
        private bool _isEventScene = false;
        private List<Actor> _plusTurnActors;        
        private int plusTurnValue { get { return _plusTurnActors.Count; } }

        // 터치 입력이나 턴 실행 명령이 가능한 상태인지 여부.
        public bool canOrdered { get { return isRunning && !isPause && onInput && (mainUnitPhase != null && mainUnitPhase.isStandby); } }        

        //================ call back ================//
        // 턴 종료.
        private Action<TurnController> _cbEndTurn;
        public event Action<TurnController> cbEndTurn
        {
            add     { _cbEndTurn -= value; _cbEndTurn += value; }
            remove  { _cbEndTurn -= value; }
        }


        private TurnController()
        {
            // unit group의 실행순서.
            _groupOrder = new List<UnitPhaseGroup>();
            _groupOrder.Add(UnitPhaseGroup.Ready);
            _groupOrder.Add(UnitPhaseGroup.Charm);
            _groupOrder.Add(UnitPhaseGroup.MercenaryReady);
            _groupOrder.Add(UnitPhaseGroup.NPC); 
            _groupOrder.Add(UnitPhaseGroup.Mercenary);
            _groupOrder.Add(UnitPhaseGroup.Tile);
            _groupOrder.Add(UnitPhaseGroup.Champion);
            _groupOrder.Add(UnitPhaseGroup.Monster);
            _groupOrder.Add(UnitPhaseGroup.EndTurn);

            _groupPhaseListSet = new Dictionary<int, List<TurnPhase>>();
            for (int i = 0, count = _groupOrder.Count; i < count; i++)
            {
                _groupPhaseListSet.Add((int)_groupOrder[i], new List<TurnPhase>());
            }

            _turnPhasePool = new Dictionary<int, List<TurnPhase>>();
            // unit phase pool 생성.
            AddToPhasePool(TurnPhaseType.Unit, 20);
            // unit phase pool 생성.
            AddToPhasePool(TurnPhaseType.UnitReady, 5);
            // relocation phase pool 생성.
            AddToPhasePool(TurnPhaseType.Relocation, 15);
            // group start phase pool 생성.
            AddToPhasePool(TurnPhaseType.GroupStart, 10);
            // group end phase pool 생성.
            AddToPhasePool(TurnPhaseType.GroupEnd, 10);
            // sync phase pool 생성.
            AddToPhasePool(TurnPhaseType.SyncPhase, 3);
            // drop phase pool 생성.
            AddToPhasePool(TurnPhaseType.DropPhase, 3);
            // tile stun phase pool 생성.
            AddToPhasePool(TurnPhaseType.TileStun, 5);
            // end turn phase pool 생성.
            AddToPhasePool(TurnPhaseType.End, 2);

            actorSeqList = new List<int>();
            _currentPhaseList = new List<TurnPhase>();
            championOrderList = new List<Actor>();
            _plusTurnActors = new List<Actor>();
            onInput = true;
        }

        public static TurnController FromDirector(BattleBaseDirector director)
        {
            if (director == null)
            {
                return null;
            }

            var turnController = new TurnController();
            turnController.director = director;

            return turnController;
        }

        //================================================================================
        // 상속 혹은 interface의 메서드 모음./
        //================================================================================
        public void OnDestroy()
        {
            Clear();
            ClearEvent();
        }

        //================================================================================
        // 새로 정의한 일반 메서드 모음./
        //================================================================================
        // pool에 phase 미리 생성.
        private void AddToPhasePool(TurnPhaseType type, int count)
        {
            var phaseList = new List<TurnPhase>();
            for (int i = 0; i < count; i++)
            {
                phaseList.Add(TurnPhase.FromControllerWithType(this, type));
            }
            _turnPhasePool.Add((int)type, phaseList);
        }

        public void UpdateState()
        {   
            if(!isRunning || isPause)            
            {
                return;
            }

            if (isPhaseEnded)
            {
                RunNextPhase();
            }
            else
            {
                if (currentPhase != null && currentPhase.isDirty)
                {
                    currentPhase.UpdatePhase();
                }
            }
        }

        // 스테이지 첫진입 혹은 재시작할 때마다 처음 설정해야 하는 경우 여기에 추가.
        public void Reset()
        {
            championOrderList.Clear();
            ClearEvent();
        }

        public void ResetBonusTurnData()
        {
            _bonusPhaseIndex = -1;
            bonusTurn = BonusTurn.None;
            isSkillBonusPhase = false;
            _plusTurnActors.Clear();            
        }

        public void Clear()
        {
            ClearPhases();

            mainUnitPhase = null;
            isSkillBonusPhase = false;
            isRunning = false;
            bonusTurn = BonusTurn.None;
        }

        private void ClearPhases()
        {
            var enumer = _groupPhaseListSet.GetEnumerator();
            while (enumer.MoveNext())
            {
                var list = enumer.Current.Value;
                for (int i = 0, count = list.Count; i < count; i++)
                {
                    ReturnTurnPhase(list[i]);
                }
                list.Clear();
            }

            _currentPhaseList.Clear();
            _bonusPhaseIndex = -1;            
            currentGroupIndex = -1;
            currentPhaseIndex = -1;            
            currentPhase = null;
            actorSeqList.Clear();
            _plusTurnActors.Clear();
        }

        // 이벤트 메신저에서 turn관련 이벤트 콜백 제거.
        public void ClearEvent()
        {
            EventMessenger.Clear(EventName.turnReady);
            EventMessenger.Clear(EventName.turnEnd);
            EventMessenger.Clear(EventName.turnChainBonus);
            EventMessenger.Clear(EventName.turnPhaseGroupChanged);
            EventMessenger.Clear(EventName.turnGroupStartReturn);
            EventMessenger.Clear(EventName.turnGroupStartTransforming);
            EventMessenger.Clear(EventName.turnGroupStartSupport);
            EventMessenger.Clear(EventName.turnGroupEndReturn);
            EventMessenger.Clear(EventName.turnSyncStandby);
            EventMessenger.Clear(EventName.turnSyncActiveStart);
            EventMessenger.Clear(EventName.turnSyncActiveEnd);
            EventMessenger.Clear(EventName.turnUnitStandBy);
            EventMessenger.Clear(EventName.turnUnitFinishAllHit);
            EventMessenger.Clear(EventName.turnUnitTransforming);
            EventMessenger.Clear(EventName.turnEndReturn);
            EventMessenger.Clear(EventName.atkSimulateBefore);
            EventMessenger.Clear(EventName.groupTurnStartMercenaryReady);
        }

        // [jhw] 현재 사용하는 phase와 이미 지나간 phase를 제외하고는 해당 actor의 UnitPhase는 모두 삭제한다.
        // 미리 배치했던 UnitPhase의 owner(즉 actor)가 죽었다가 재활용되면 기존 UnitPhase가 stand by에 걸리고 넘어갈 수 없게된다.
        // 따라서 캐릭터가 죽는 시점에 UnitPhase를 삭제하도록 하기 위해 추가했다.
        public void RemoveUnitPhase(Actor actor)
        {
            Debug.Log("TurnController.RemoveUnitPhase Start : " + actor);

            if (actor == null)
            {
                return;
            }

            var allowedRoleGroup = RoleGroup.GroupUnitWithOutNormal;
            if ((actor.role.roleGroup & allowedRoleGroup) == 0)
            {
                return;
            }

            // 현재 그룹 phase 이 후에 있는 것만 삭제한다.
            var groupIndex = (currentGroupIndex < 0) ? -1 : currentGroupIndex - 1;
            while (true)
            {
                groupIndex++;
                if (groupIndex >= _groupOrder.Count)
                {
                    break;
                }

                var groupPhase = _groupOrder[groupIndex];
                var tempGroupList = _groupPhaseListSet[(int)groupPhase];
                // 현재 group인 경우, current phase 이후에만 삭제한다.
                var targetIndex = (groupIndex == currentGroupIndex) ? tempGroupList.IndexOf(currentPhase) + 1 : 0;

                for (int i = tempGroupList.Count - 1; i >= targetIndex; i--)
                {
                    var phase = tempGroupList[i];
                    if (phase.type != TurnPhaseType.Unit || phase.Equals(mainUnitPhase))
                    {
                        continue;
                    }

                    // 해당 actor의 unit phase나 owner가 null이면 삭제한다.
                    if (!phase.isRunning && (actor.Equals(phase.owner) || phase.owner == null))
                    {
                        Debug.Log("TurnController.RemoveUnitPhase remove in next group. phaseOwner : " + phase.owner);

                        tempGroupList.Remove(phase);
                        ReturnTurnPhase(phase);
                    }
                }
            }

            // 현재 list에 등록된 것도 삭제한다.            
            for (int i = _currentPhaseList.Count - 1; i >= currentPhaseIndex + 1; i--)
            {
                var phase = _currentPhaseList[i];
                if (phase.type != TurnPhaseType.Unit || phase.Equals(mainUnitPhase))
                {
                    continue;
                }

                // 해당 actor의 unit phase나 owner가 null이면 삭제한다.
                if (!phase.isRunning && (actor.Equals(phase.owner) || phase.owner == null))
                {
                    Debug.Log("TurnController.RemoveUnitPhase remove in current group. phaseOwner : " + phase.owner);

                    _currentPhaseList.Remove(phase);
                    ReturnTurnPhase(phase);
                }
            }
        }

        // 매턴마다 반복되며 실행되는 함수.
        public void StartTurn()
        {
            if (isRunning)
            {
                return;
            }

            isRunning = true;

            Ready();

            EventMessenger.Send(EventName.turnStart, this);

            RunNextPhase();
        }

        // 이벤트씬 전투에 실행되는 함수.
        public void EventTurn()
        {
            if (isRunning)
            {
                return;
            }

            isRunning = true;

            EventMessenger.Send(EventName.turnReady, director);

            currentPhaseIndex = -1;
            RunNextPhase();
        }

        public void SetEventScene(bool isEvent, Actor actor = null)
        {
            _isEventScene = isEvent;
            if (_isEventScene == true && actor != null)
            {
                Debug.Log(CTag.yellow("EventActionActor : " + actor));
                Clear();
                SetMercenaryReadyPhase();
                SetEventPhase(actor);
                SetCurrentPhaseList();
                currentPhase = GetNextPhase();
            }
            else
            {
                Ready();
            }
        }

        public void SetPause(bool pause)
        {
            Debug.Log(CTag.yellow("TurnController Pause : " + pause));
            isPause = pause;
        }

        public void SetInput(bool on)
        {
            onInput = on;
        }

        public void EnableMainUnit()
        {
            if(!canOrdered)
            {
                return;
            }

            director.skillReadyMercenary = null;
            var userName = string.Empty;
            var toastStr = TextPool.GetText("sys_raid_noti_change_turn");
            if (director.stage.stage_type == StageType.MultiRaid)
            {
                var userInfo = MultiRaidManager.instance.GetUserInfo(director.directorMode.controlUserSeq);
                userName = userInfo?.userName;
            }
            if (director.stage.stage_type == StageType.PvP)
            {
                PvpUserInfoPvo userPvo = null;
                if (BattleStage.instance.director.directorMode.controlUserSeq == BattleStage.instance.homeUserPvo.seq)
                {
                    userPvo = BattleStage.instance.homeUserPvo;
                }
                else
                {
                    userPvo = BattleStage.instance.awayUserPvo;
                }
                userName = userPvo?.name;
            }

            if (string.IsNullOrEmpty(userName) == false)
            {
                UIManager.instance.ShowToast(string.Format(toastStr, userName));
            }

            EventMessenger.Send(EventName.btReadyAction, this);

            if (director.isAuto == false && isEventScene == false)
            {
                director.ShowSelectPcAll();
                BattleStage.instance.SetTouchable(true);
            }
        }

        // 메인 유닛 행동 대기 상태를 pass한다.
        public void PassMainUnitPhase()
        {
            if(mainUnitPhase == null)
            {
                return;
            }

            ResetBonusTurnData();
            // chain 관련 보너스 처리가 있다면 리셋한다.
            foreach (var candidate in mainUnitPhase.candidateList)
            {
                candidate?.ResetChainBonus();
            }            
            BattleSimulator.instance.ResetChainInfo();            

            mainUnitPhase.currentOrder?.End();
        }

        private void Ready()
        {
            //if(_isEventScene == true)
            //{
            //    SetEventScene(_isEventScene);
            //}
            ClearPhases();

            //======= 턴시작전, 미리 배치되어야 하는 유닛 구성(스폰이라던지...) ========//     
            SetReadyPhase();
            //======= 매혹에 걸린 용병 group phase 구성 ========//     
            SetCharmMercenaryPhase();
            //======= 드라군 엑티브, 팔랑크스 체인 등 group phase 구성 ========//     
            SetMercenaryReadyPhase();
            //======= NPC group phase 구성 ========//     
            SetNPCPhase();
            //======= 용병 group phase 구성 ========//     
            SetMercenaryPhase();
            //======= 타일환경 오브젝트 phase 구성 ========//     
            SetTilePhase();
            //======= 챔피온(AI용병) group phase 구성 ========//
            SetChampionPhase();
            //======= 몬스터(엘리트, 보스) group phase 구성 ========//
            SetSpecialMobPhase();
            //======= 턴 마무리 phase 구성 ========//   
            SetEndTurnPhase();

            BattleSimulator.instance.Prepare();            

            EventMessenger.Send(EventName.turnReady, director);
            EventMessenger.Send(EventName.playActorOrderEnd, this, actorSeqList);
        }

        private void RunNextPhase()
        {
            isPhaseEnded = false;
            while (true)
            {
                if(isPause)
                {
                    isPhaseEnded = true;
                    return;
                }

                var nextPhase = GetNextPhase();
                if (nextPhase != null)
                {
                    // [jhw] nextPhase가 실행을 실패하면 update를 통하지 않고 while문에 의해 곧바로 다음 next phase를 얻어와서 실행하도록 수정.
                    nextPhase.cbEnd += OnEndPhase;
                    if (nextPhase.Run())
                    {
                        break;
                    }
                }
                else
                {
                    End();
                    break;
                }
            }
        }

        private void End()
        {
            var cb = _cbEndTurn;
            cb?.Invoke(this);

            Clear();   
        }        

        // pool에서 turn phase 가져오기.
        private TurnPhase PickupTurnPhase(TurnPhaseType phaseType)
        {
            var key = (int)phaseType;
            var phasePool = _turnPhasePool[key];

            TurnPhase phase = null;
            if(phasePool.Count > 0)
            {   
                phase = phasePool[0];
                phasePool.RemoveAt(0);
            }
            else
            {
                phase = TurnPhase.FromControllerWithType(this, phaseType);
            }

            return phase;
        }

        // pool로 turn phase 회수.
        private void ReturnTurnPhase(TurnPhase phase)
        {
            if (phase == null)
            {
                return;
            }

            var phasePool = _turnPhasePool[(int)phase.type];
            if (!phasePool.Contains(phase))
            {
                phase.Clear();
                phasePool.Add(phase);
            }
        }

        private TurnPhase GetNextPhase()
        {
            currentPhaseIndex++;
            if (currentPhaseIndex < 0 || currentPhaseIndex >= _currentPhaseList.Count)
            {
                if(!SetCurrentPhaseList())
                {
                    currentPhase = null;
                    return null;
                }
                currentPhaseIndex = 0;
            }

            currentPhase = _currentPhaseList[currentPhaseIndex];

            return currentPhase;
        }

        // turn phase 그룹 교체.
        private bool SetCurrentPhaseList()
        {
            _currentPhaseList.Clear();

            while (true)
            {
                currentGroupIndex++;
                if (currentGroupIndex < 0 || currentGroupIndex >= _groupOrder.Count)
                {
                    return false;
                }

                currentGroupPhase = _groupOrder[currentGroupIndex];
                BattleSimulator.instance.isComboSave = true;
                var groupList   = _groupPhaseListSet[(int)currentGroupPhase];
                if (groupList.Count > 0)
                {
                    _currentPhaseList.AddRange(groupList);
                    EventMessenger.Send(EventName.turnPhaseGroupChanged, this, _currentPhaseList);
                    return true;
                }
            }
        }

        // 턴 시작전 준비해야되는 것들 구성.(ex:스폰유닛)
        private void SetReadyPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Ready];
            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));

            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 매혹 페이즈 적용.
        private void SetCharmMercenaryPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Charm];
            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 움직일 타일이 없을 경우 스턴상태로 전환하는 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.TileStun));

            //임시 테스트용
            var unitList = director.directorMode.GetControlUnitList();
            List<Actor> charmList = new List<Actor>();

            for (int i = 0; i < unitList.Count; i++)
            {
                var charmActor = unitList[i];
                if (charmActor.role.buffManager.IsAttachedShowing(BuffName.BUFF_ELITE_CHARM) ||
                    charmActor.role.buffManager.IsAttachedShowing(BuffName.BUFF_MERCENARY_CHARM))
                {
                    charmList.Add(charmActor);
                }
            }

            if (charmList.Count > 0)
            {
                for (int i = 0, count = charmList.Count; i < count; i++)
                {
                    var actor = charmList[i];
                    // ai가 없는경우는 움직일 수 없으므로 건너뛴다.
                    if ((actor.role.aiHandler == null && actor.role.aIController == null) || actor.restTurn > 1)
                    {
                        continue;
                    }

                    var phase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
                    phase.Reset(actor);
                    actorSeqList.Add(actor.seqId);
                    phaseList.Add(phase);
                    // standby order로 ai 동작 액션 등록.                    
                    var standbyOrder = phase.GetOrder(TurnPhaseOrderType.StandBy);
                    actor.SetOrderActionAITarageting(standbyOrder);

                    // 아이템 드랍 페이즈 추가.
                    phaseList.Add(PickupTurnPhase(TurnPhaseType.DropPhase));

                    // 일반 특수형 몹들은 모든 몹들이 움직이고 난 이후에 재배치가 이루어지므로 마지막 한번만 재배치 phase를 추가한다.
                    phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
                }
            }

            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 용병 준비 페이즈 적용.
        private void SetMercenaryReadyPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.MercenaryReady];
            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 움직일 타일이 없을 경우 스턴상태로 전환하는 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.TileStun));
            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // NPC 페이즈 적용.
        private void SetNPCPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.NPC];

            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));

            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));

            var npcUnitList = BattleStage.instance.director.GetActorGroup(RoleGroup.NPC);
            if (npcUnitList.Count > 0)
            {
                for (int i = 0, count = npcUnitList.Count; i < count; i++)
                {
                    var actor = npcUnitList[i];

                    // ai가 없는경우는 움직일 수 없으므로 건너뛴다.
                    if ((actor.role.aiHandler == null && actor.role.aIController == null) || actor.restTurn > 1)
                    {
                        continue;
                    }

                    var phase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
                    phase.Reset(actor);
                    actorSeqList.Add(actor.seqId);
                    phaseList.Add(phase);

                    // standby order로 ai 동작 액션 등록.                    
                    var standbyOrder = phase.GetOrder(TurnPhaseOrderType.StandBy);
                    actor.SetOrderActionAITarageting(standbyOrder);
                }

                // 아이템 드랍 페이즈 추가.
                phaseList.Add(PickupTurnPhase(TurnPhaseType.DropPhase));

                // 몬스터 재배치 페이즈 추가.
                phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            }
            
            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 용병 페이즈 적용.
        private void SetMercenaryPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Mercenary];
            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 움직일 타일이 없을 경우 스턴상태로 전환하는 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.TileStun));

            // 메인 용병의 unit phase 추가.           
            var unitPhase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
            // [jhw] 초기화 과정에서 mainUnitPhase인지 체크하는 경우가 있으므로 항상 mainUnitPhase 지정 및 list에 add한 후,
            // 추가 메서드를 호출하도록 한다.
            mainUnitPhase = unitPhase;
            phaseList.Add(unitPhase);
            unitPhase.Reset(null, director.directorMode.GetControlUnitList());           

            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 이벤트 페이즈 적용.
        private void SetEventPhase(Actor actor)
        {
            UnitPhaseGroup phase = UnitPhaseGroup.None;
            switch (actor.role.roleGroup)
            {
                case RoleGroup.SpecialMob:
                case RoleGroup.Boss:
                    phase = UnitPhaseGroup.Monster;
                    break;
                case RoleGroup.Champion:
                    phase = UnitPhaseGroup.Champion;
                    break;
                default:
                    phase = UnitPhaseGroup.Mercenary;
                    break;
            }
            var phaseList = _groupPhaseListSet[(int)phase];
            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));

            // 이벤트 용병의 unit phase 추가.           
            var unitPhase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;

            mainUnitPhase = unitPhase;
            phaseList.Add(unitPhase);
            unitPhase.Reset(actor);

            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 타일 환경 오브젝트 페이즈 적용.
        private void SetTilePhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Tile];

            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 동시 개체 실행 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.SyncPhase));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 챔피온(AI용병) 페이즈 적용.
        private void SetChampionPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Champion];

            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));

            // 신규리스트 정보로 기존 order list를 재구성한다.
            var championList = BattleStage.instance.director.GetActorGroup(RoleGroup.Champion);
            ArrangeChampionOrderList(championList);

            // 챔피언 타입은 한 턴에 하나만 움직이므로 움직일 챔피언을 가져온다.
            // order list의 0번에서 꺼내서 등록하고 리스트 마지막으로 옮긴다.
            if (championOrderList.Count > 0)
            {
                Actor mainChampion = null;
                for (int i = 0, count = championOrderList.Count; i < count; i++)
                {
                    if (championOrderList[i].restTurn == 1)
                    {
                        mainChampion = championOrderList[i];
                        break;
                    }
                }

                if (mainChampion != null)
                {
                    championOrderList.Remove(mainChampion);
                    championOrderList.Add(mainChampion);
                    
                    var phase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
                    phase.Reset(mainChampion);
                    actorSeqList.Add(mainChampion.seqId);
                    phaseList.Add(phase);
                    // standby order로 ai 동작 액션 등록.                    
                    var standbyOrder = phase.GetOrder(TurnPhaseOrderType.StandBy);
                    mainChampion.SetOrderActionAITarageting(standbyOrder);
                    // 몬스터 재배치 페이즈 추가.            
                    phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));              
                }
            }

            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // champion order list 재구성.
        public void ArrangeChampionOrderList(List<Actor> newList)
        {
            if (newList == null || newList.Count <= 0)
            {
                return;
            }

            for (int i = 0, count = newList.Count; i < count; i++)
            {
                var champion = newList[i];
                // 없는 챔피언은 신규로 등록.
                if (!championOrderList.Contains(champion))
                {
                    championOrderList.Add(champion);
                }
            }
            
            for (int i = championOrderList.Count-1; i >= 0 ; i--)
            {
                var champion = championOrderList[i];
                // 새로받은 챔피언 리스트에 없으면 제거된 것이므로 remove 한다.
                if (!newList.Contains(champion))
                {
                    championOrderList.RemoveAt(i);
                }
            }
        }

        // 특수몹(엘리트, 보스) 페이즈 적용.
        private void SetSpecialMobPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.Monster];

            // 그룹 시작 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupStart));
            // 틱으로 연계해서 타일에 빈공간이 생길 수 있으니 재배치
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));

            var specialMobList = GetSpecialMobList();
            if (specialMobList.Count > 0)
            {
                for (int i = 0, count = specialMobList.Count; i < count; i++)
                {
                    var actor = specialMobList[i];
                    // ai가 없는경우는 움직일 수 없으므로 건너뛴다.
                    if((actor.role.aiHandler == null && actor.role.aIController == null) || actor.restTurn > 1)
                    {
                        continue;
                    }

                    var phase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
                    phase.Reset(actor);
                    actorSeqList.Add(actor.seqId);
                    phaseList.Add(phase);
                    // standby order로 ai 동작 액션 등록.                    
                    var standbyOrder = phase.GetOrder(TurnPhaseOrderType.StandBy);
                    actor.SetOrderActionAITarageting(standbyOrder);
                }

                // 아이템 드랍 페이즈 추가.
                phaseList.Add(PickupTurnPhase(TurnPhaseType.DropPhase));

                // 일반 특수형 몹들은 모든 몹들이 움직이고 난 이후에 재배치가 이루어지므로 마지막 한번만 재배치 phase를 추가한다.
                phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
            }

            // 그룹 종료 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.GroupEnd));
        }

        // 특수몹 리스트를 행동 순서에 맞게 처리하여 가져옴.
        public List<Actor> GetSpecialMobList()
        {
            var eliteMobList = BattleStage.instance.director.GetActorGroup(RoleGroup.SpecialMob);
            var bossMobList = BattleStage.instance.director.GetActorGroup(RoleGroup.Boss);
            var specialMobList = new List<Actor>();
            specialMobList.AddRange(eliteMobList);
            specialMobList.AddRange(bossMobList);

            var orderList = new List<int>();
            var orderedActorSet = new Dictionary<int, List<Actor>>();
            var orderedActorList = new List<Actor>();
            var noneOrderActorList = new List<Actor>();
            // 우선행동몬스터 가 있는지 확인하고 해당 order에 맞게 저장.
            for (int i = 0; i < specialMobList.Count; i++)
            {
                // 행동 순서 지정이 없으면 일단 패스.
                var actor = specialMobList[i];
                if (actor.isDie == true)
                {
                    //Debug.LogError("TurnController special mob setting : " + actor + " : " + actor.isDie);
                    continue;
                }
                
                //actor의 행동 order를 가져옴
                int order = actor.role.data.actionOrder;
                if(order == 0)
                {
                    noneOrderActorList.Add(actor);
                    continue;
                }

                //해당 order에 actor저장.
                if (!orderedActorSet.ContainsKey(order))
                {
                    orderedActorSet.Add(order, new List<Actor>());
                }
                orderedActorSet[order].Add(actor);

                //order리스트에 order저장.
                if (!orderList.Contains(order))
                {
                    orderList.Add(order);
                }
            }

            // order 정렬.
            orderList.Sort();
            // order순서대로 우선 행동 몬스터 리스트 저장.
            for (int i = 0; i < orderList.Count; i++)
            {
                orderedActorList.AddRange(orderedActorSet[orderList[i]]);
            }
            // 순서가 없어서 누락된 actor는 마지막에 넣어준다.
            for (int i = 0; i < noneOrderActorList.Count; i++)
            {
                orderedActorList.Add(noneOrderActorList[i]);
            }

            return orderedActorList;
        }

        // 턴 마무리 페이즈 적용.
        private void SetEndTurnPhase()
        {
            var phaseList = _groupPhaseListSet[(int)UnitPhaseGroup.EndTurn];
            // 턴 종료 처리 페이즈 추가.
            phaseList.Add(PickupTurnPhase(TurnPhaseType.End));
            // 몬스터 재배치 페이즈 추가.            
            phaseList.Add(PickupTurnPhase(TurnPhaseType.Relocation));
        }

        private void ResetForBonusPhase()
        {
            // 기타 시스템 리셋.
            TouchSensor.instance.SetTouchTimer(null);
            BattleSimulator.instance.Prepare();

            EventMessenger.Send(EventName.turnReady, director);       
        }        

        // 체인보너스 발생시, 관련 설정.
        private void SetChainBonusPhase()
        {
            ResetForBonusPhase();

            var owner = director.mainMercenary;
            // phase 설정.
            var unitPhase = _currentPhaseList[_bonusPhaseIndex] as UnitPhase;
            unitPhase.Reset(owner);
            // GetNextPhase()에서 돌아갈 phase를 가져와야 하므로 이전에 저장한 index에 -1을 해서 next로 돌아갈 인덱스로 찾을 수 있도록 처리한다.
            currentPhaseIndex = _bonusPhaseIndex - 1;
            _bonusPhaseIndex = -1;

            var chainPhase = BattleSimulator.instance.currentChainPhase;
            var textKey = string.Format("chain_report_0{0}", chainPhase + 1);
            if (string.IsNullOrEmpty(textKey) == false)
            {
                UIManager.instance.ShowBattleToast(TextPool.GetText(textKey));
            }
            BattleSimulator.instance.EndChainBonus();
            // 이벤트 보내기.            
            EventMessenger.Send(EventName.turnChainBonus, this, owner);
            EventMessenger.Send(EventName.playActorOrderBonus, this, actorSeqList);
        }

        // 스킬 사용에 따라 보너스 phase 발생시, 관련 설정.
        private void SetSkillBonusPhase()
        {
            var owner = director.mainMercenary;
            if (owner.isDie == false)
            {
                ResetForBonusPhase();

                var skillBehavior = director.actActor.role.GetFirstSkill();
                director.actActor.role.currentLinkCount = 0;
                if (skillBehavior.isFailedSkill == true)
                {
                    var msg = TextPool.GetText(skillBehavior.str_failed);
                    if (string.IsNullOrEmpty(msg) == false)
                    {
                        UIManager.instance.ShowToast(msg);
                    }
                }
                else
                {
                    UIManager.instance.ShowBattleToast(TextPool.GetText("sys_ative_bouns_trun"));
                }
            }

            isSkillBonusPhase = false;
            var ownerSeq = currentPhase.candidateList.Count > 1 ? 0 : owner.seqId;
            if (actorSeqList.Count > 0)
            {
                actorSeqList[0] = ownerSeq;
            }
            else
            {
                actorSeqList.Add(ownerSeq);
            }
            EventMessenger.Send(EventName.turnSkillBonus, this, owner);
            EventMessenger.Send(EventName.playActorOrderBonus, this, actorSeqList);
        }

        public void AddPlusTurnActor(Actor plusActor)
        {
            if (isEventScene == false)
            {
                //[DJ] 같은캐릭터가 여러번 들어갈 수 있으니 중복체크 안함
                _plusTurnActors.Add(plusActor);
            }
        }

        // 턴 추가하는 스킬에 따라 보너스 phase 발생시, 관련 설정.
        private void SetPlusTurn()
        {
            ResetForBonusPhase();

            var plusTurnUnit = _plusTurnActors[0];
            var allMercenary = director.directorMode.GetCurrentMercenaryList();
            foreach (var mercenary in allMercenary)
            {
                mercenary.isAutoPlay = false;
                mercenary.isAutoActivePlay = false;
            }
            
            // phase 설정.
            var unitPhase = _currentPhaseList[_bonusPhaseIndex] as UnitPhase;
            unitPhase.Reset(plusTurnUnit);
            // GetNextPhase()에서 돌아갈 phase를 가져와야 하므로 이전에 저장한 index에 -1을 해서 next로 돌아갈 인덱스로 찾을 수 있도록 처리한다.
            currentPhaseIndex = _bonusPhaseIndex - 1;
            _bonusPhaseIndex = -1;
            _plusTurnActors.RemoveAt(0);            

            UIManager.instance.ShowBattleToast(TextPool.GetText("sys_plus_bouns_trun"));

            EventMessenger.Send(EventName.turnPlusBonus, this, plusTurnUnit);
            EventMessenger.Send(EventName.playActorOrderBonus, this, actorSeqList);
        }

        // target phase의 앞에 새로운 unit phase를 삽입한다.
        public void InsertUnitPhase(UnitPhaseInsertData insertData, TurnPhaseData targetData)
        {
            // target unit phase 찾기.
            var targetPhase = FindUnitPhase(targetData.owner, targetData.group);
            InsertUnitPhase(insertData, targetPhase);
        }

        // target phase의 앞에 새로운 unit phase를 삽입한다.
        public UnitPhase InsertUnitPhase(UnitPhaseInsertData insertData, TurnPhase targetPhase)
        {
            if (insertData.owner == null || targetPhase == null)
            {
                return null;
            }

            // target phase의 data 가져오기.
            var targetData = targetPhase.GetPhaseData();
            insertData.index = targetData.index;            
            // 즉시 실행 flag가 있으면 group을 동기화 한다.
            if (insertData.immediatelyStart)
            {
                insertData.group = targetData.group;
            }

            // scrData 기반으로 unit phase 구성.            
            var phaseList = _groupPhaseListSet[(int)insertData.group];
            var unitPhase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
            unitPhase.Reset(insertData.owner);
            unitPhase.allowedStates = insertData.allowedStates;
            phaseList.Insert(insertData.index, unitPhase);

            // 재배치를 사용한다면 페이즈 추가.
            TurnPhase relocationPhase = null;
            if (!insertData.ignoreRelocation)
            {                
                relocationPhase = PickupTurnPhase(TurnPhaseType.Relocation);
                phaseList.Insert(insertData.index + 1, relocationPhase);
            }            

            // 현재 같은 그룹이면 current list에도 추가한다.            
            if (insertData.group == currentGroupPhase)
            {
                for(int i = 0, count = _currentPhaseList.Count; i < count; i++)
                {
                    var phase = _currentPhaseList[i];
                    if(targetPhase.Equals(phase))
                    {
                        _currentPhaseList.Insert(i, unitPhase);
                        if(relocationPhase != null)
                        {
                            _currentPhaseList.Insert(i + 1, relocationPhase);
                        }                        
                        break;
                    }
                }
            }

            // 즉시 실행이면 현재 phase를 reset하고 신규 phase를 실행할수 있도록 설정한다.
            if (insertData.immediatelyStart)
            {
                currentPhase.Reset();

                // next phase 실행시에 현재 삽입한 phase가 실행될 수 있도록 -1하여 currentPhaseIndex를 설정한다.
                currentPhaseIndex = insertData.index - 1;
                isPhaseEnded = true;
            }

            return unitPhase;
        }

        public UnitPhase AddUnitPhase(UnitPhaseInsertData addData)
        {
            if (addData.owner == null)
            {
                return null;
            }

            // scrData 기반으로 unit phase 구성.            
            var phaseList = _groupPhaseListSet[(int)addData.group];
            var unitPhase = PickupTurnPhase(TurnPhaseType.Unit) as UnitPhase;
            unitPhase.Reset(addData.owner);
            unitPhase.allowedStates = addData.allowedStates;
            phaseList.Add(unitPhase);

            // 재배치를 사용한다면 페이즈 추가.
            TurnPhase relocationPhase = null;
            if (!addData.ignoreRelocation)
            {
                relocationPhase = PickupTurnPhase(TurnPhaseType.Relocation);
                phaseList.Add(relocationPhase);
            }

            // 현재 같은 그룹이면 current list에도 추가한다.            
            if (addData.group == currentGroupPhase)
            {
                _currentPhaseList.Add(unitPhase);
                if (relocationPhase != null)
                {
                    _currentPhaseList.Add(relocationPhase);
                }
            }

            return unitPhase;
        }

        // [191206][jhw] phase 삽입 조건 추가. false면 targetPhase앞에, true면 targetPhase뒤에 삽입한다.        
        public UnitReadyPhase InsertUnitReadyPhase(UnitReadyPhaseInsertData insertData, TurnPhase targetPhase, bool insertBack)
        {
            if (insertData.owner == null || targetPhase == null)
            {
                return null;
            }

            // target phase의 data 가져오기.
            var targetData = targetPhase.GetPhaseData();
            insertData.index = (insertBack) ? targetData.index + 1 : targetData.index;
            // 즉시 실행 flag가 있으면 group을 동기화 한다.
            if (insertData.immediatelyStart)
            {
                insertData.group = targetData.group;
            }

            // scrData 기반으로 unit phase 구성.            
            var phaseList = _groupPhaseListSet[(int)insertData.group];
            var unitPhase = PickupTurnPhase(TurnPhaseType.UnitReady) as UnitReadyPhase;
            unitPhase.Reset(insertData.owner);
            unitPhase.allowedStates = insertData.allowedStates;
            phaseList.Insert(insertData.index, unitPhase);

            // 재배치를 사용한다면 페이즈 추가.
            TurnPhase relocationPhase = null;
            if (!insertData.ignoreRelocation)
            {
                relocationPhase = PickupTurnPhase(TurnPhaseType.Relocation);
                phaseList.Insert(insertData.index + 1, relocationPhase);
            }

            // 현재 같은 그룹이면 current list에도 추가한다.            
            if (insertData.group == currentGroupPhase)
            {
                for (int i = 0, count = _currentPhaseList.Count; i < count; i++)
                {
                    var phase = _currentPhaseList[i];
                    if (targetPhase.Equals(phase))
                    {
                        var insertIndex = (insertBack) ? i+1 : i;
                        _currentPhaseList.Insert(insertIndex, unitPhase);
                        if (relocationPhase != null)
                        {
                            _currentPhaseList.Insert(insertIndex + 1, relocationPhase);
                        }
                        break;
                    }
                }
            }

            // 즉시 실행이면 현재 phase를 reset하고 신규 phase를 실행할수 있도록 설정한다.
            if (insertData.immediatelyStart)
            {
                currentPhase.Reset();

                // next phase 실행시에 현재 삽입한 phase가 실행될 수 있도록 -1하여 currentPhaseIndex를 설정한다.
                currentPhaseIndex = insertData.index - 1;
                isPhaseEnded = true;
            }

            return unitPhase;
        }

        // 특정 turn phase가 어디 위치에 속해 있는지를 가져오는 메서드.
        // 보통 group은 고정이지만 index는 변할 수 있으므로 필요할 때마다 position을 가져와야 한다.
        public bool GetPosition(TurnPhase target, out UnitPhaseGroup group, out int index)
        {
            group = UnitPhaseGroup.None;
            index = -1;
            if (target == null)
            {
                return false;
            }

            var enumer = _groupPhaseListSet.GetEnumerator();
            while (enumer.MoveNext())
            {
                var key = enumer.Current.Key;
                var list = enumer.Current.Value;
                for (int i = 0, count = list.Count; i < count; i++)
                {
                    var phase = list[i];
                    if (target.Equals(phase))
                    {
                        group = (UnitPhaseGroup)key;
                        index = i;
                        return true;
                    }
                }
            }

            return false;
        }

        public bool ExistUnitPhaseInCurrentGroup()
        {
            return ExistUnitPhase(currentGroupPhase);
        }

        public bool ExistUnitPhase(UnitPhaseGroup group)
        {
            var exist = false;
            var list = _groupPhaseListSet[(int)group];
            for (int i = 0, count = list.Count; i < count; i++)
            {
                var phase = list[i];
                if (phase.type == TurnPhaseType.Unit)
                {
                    exist = true;
                    break;
                }
            }

            return exist;
        }

        // 해당 owner를 가지고 있는 UnitPhase를 찾아서 돌려줌. 여러개가 있을 경우, 가장먼저 발견된 것을 돌려준다.
        private UnitPhase FindUnitPhase(Actor owner, UnitPhaseGroup group)
        {
            if (owner == null)
            {
                return null;
            }

            if (group == UnitPhaseGroup.None)
            {
                var enumer = _groupPhaseListSet.GetEnumerator();
                while (enumer.MoveNext())
                {
                    var key = enumer.Current.Key;
                    var list = enumer.Current.Value;
                    for (int i = 0, count = list.Count; i < count; i++)
                    {
                        var phase = list[i];
                        if (phase.type == TurnPhaseType.Unit && owner.Equals(phase.owner))
                        {
                            return phase as UnitPhase;
                        }
                    }
                }
            }
            else
            {
                var list = _groupPhaseListSet[(int)group];
                for (int i = 0, count = list.Count; i < count; i++)
                {
                    var phase = list[i];
                    if (phase.type == TurnPhaseType.Unit && owner.Equals(phase.owner))
                    {
                        return phase as UnitPhase;
                    }
                }
            }

            return null;
        }

        public TurnPhase GetFirstTurnPhase(UnitPhaseGroup group, TurnPhaseType phaseType)
        {
            var list = _groupPhaseListSet[(int)group];
            for (int i = 0, count = list.Count; i < count; i++)
            {
                var phase = list[i];
                if (phase.type == phaseType)
                {
                    return phase;
                }
            }

            return null;
        }

        public TurnPhase GetLastTurnPhase(UnitPhaseGroup group, TurnPhaseType phaseType)
        {
            var list = _groupPhaseListSet[(int)group];
            for (int i = list.Count-1, count = 0; i >= count; i--)
            {
                var phase = list[i];
                if (phase.type == phaseType)
                {
                    return phase;
                }
            }

            return null;
        }

        public TurnPhase GetTurnPhase(UnitPhaseGroup group, int index)
        {
            var list = _groupPhaseListSet[(int)group];
            if (index < 0 || index >= list.Count)
            {
                return null;
            }

            return list[index];
        }

        public EndPhase GetEndPhase()
        {
            var list = _groupPhaseListSet[(int)UnitPhaseGroup.EndTurn];
            for(int i = 0, count = list.Count; i < count; i++)
            {
                var phase = list[i];
                if (phase.type == TurnPhaseType.End)
                {
                    return phase as EndPhase;
                }
            }

            return null;
        }

        //================================================================================
        // 콜백이나 delegate, 이벤트 처리 메서드./
        //================================================================================
        private void OnEndPhase(TurnPhase phase, bool isEndedNormally)
        {
            // battle result 체크.            
            var battleResult = director.directorMode.CheckBattleResult();
            Debug.Log("battleResult : " + battleResult);

            if (battleResult == BattleResult.None)
            {
                switch (phase.type)
                {
                    case TurnPhaseType.Unit:
                        // owner가 null일 경우에도 무조건 초기화 해야하는 작업이 있을 경우, 아래에 추가.
                        if(phase.owner == null)
                        {                            
                            EventMessenger.Send(EventName.turnHitComboReset, this, phase);
                        }
                        else if((phase.owner.role.roleGroup & (RoleGroup.Mercenary | RoleGroup.Champion)) > 0)
                        {
                            phase.owner.role.ResetForPhaseEnd();
                            if (BattleSimulator.instance.isChainBonus || plusTurnValue > 0)
                            {
                                EventMessenger.Send(EventName.turnHitComboReset, this, phase);
                                _bonusPhaseIndex = currentPhaseIndex;                                
                            }
                            else
                            {
                                phase.owner.role.targetingBehavior.ResetCallback();
                                if (isSkillBonusPhase == false)
                                {
                                    EventMessenger.Send(EventName.turnHitComboReset, this, phase);
                                }
                                else
                                {
                                    BattleStage.instance.director.directorMode.SetAutoAssistCallback(phase.owner);
                                }
                                EventMessenger.Send(EventName.showRaidScore);
                            }
                        }
                        break;

                    case TurnPhaseType.Relocation:
                        bonusTurn = BonusTurn.None;
                        if (isChainBonusPhase && BattleSimulator.instance.isChainBonus)
                        {
                            bonusTurn = BonusTurn.Chain;
                            SetChainBonusPhase();                                                 
                        }
                        else if (isSkillBonusPhase)
                        {
                            if (isEventScene == false)
                            {
                                bonusTurn = BonusTurn.Skill;
                                SetSkillBonusPhase();
                            }
                        }
                        else if (plusTurnValue > 0)
                        {
                            bonusTurn = BonusTurn.Addition;
                            SetPlusTurn();                            
                        }

                        if (isBonusTurn)
                        {
                            director.directorMode.EndTurn((int)bonusTurn);
                        }
                        break;
                }

                // 전투 결과가 나오지 않은 경우는 일반 진행.
                isPhaseEnded = isEndedNormally;
            }
            else
            {
                // 이미 전투 결과가 나온경우, 중간의 다른 phase는 건너뛰고 바로 relocation phase로 이동한다.
                while (true)
                {
                    var nextPhase = GetNextPhase();
                    if (nextPhase == null)
                    {
                        // relocation phase 없이 null이 나오면 잘못된 것이므로 확인 필요.
                        Debug.Assert(nextPhase != null);
                        break;
                    }
                    else if (nextPhase.type == TurnPhaseType.Relocation)
                    {
                        // 전투 결과처리를 하고 종료할 것이므로 call back을 등록하지 않고 실행한다.                        
                        nextPhase.Run();
                        break;
                    }
                    else
                    {
                        nextPhase.Clear();
                    }
                }
            }            
        }
	}
}