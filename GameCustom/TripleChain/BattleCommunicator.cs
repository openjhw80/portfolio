using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using LitJson;
using Eureka;


namespace Eureka
{
    // [jhw] 해당 order는 순서로 지정된 값에 따라 전환가능 여부를 판단한다. 따라서 맘대로 순서를 변경하면 안된다.
    // ex) BattleEndRequest를 하고 대기중인 상태에서 BattleEnd가 오면 높은값을 가지므로 전환가능하다.
    // 뒤늦게 혹은 오류로 인해 TurnEnd 이하의 order가 전달되도 BattleEndRequest가 높은값이므로 이하값은 무시하게 된다.
    // 기존 order에 +1을 하면서 증가시키므로 bit flag 값으로 사용할 수 없다.
    public enum BattleOrder
    {
        None,

        // 전투 시작단계.
        BattleStartRequest,
        BattleStart,

        // 턴 시작단계.
        TurnStartRequest,
        TurnStart,

        // action 입력 준비 단계.
        ReadyActionRequest,
        ReadyAction,

        // action 실행 단계.        
        TimeoutRequest, // turn 제한 시간 내에 action run이 오지 않은 경우에 대한 확인 request 상태.
        ActionWait,
        ActionRun,

        // 턴 종료단계.
        TurnEndRequest,
        TurnEnd,    // turn end 다음에는 전투가 종료된 것이 아니라면 turn start로 전환되어야 한다.

        // 전투 종료단계.
        BattleEndRequest,   // 전투 종료 서버 알림 단계(give up이나 clear 등)
        BattleEndResult,    // 전투 종료를 확인하고 결과 표시 대기 중.
        BattleEnd,          // 전투 결과 포함 모든 알림 처리 완료. 스테이지 종료만을 기다리는 상태.
    }

    public abstract class BattleCommunicator : IBattleCommonListener
    {
        protected static float delayTimeUI;

        protected DirectorMode _directorMode;
        protected BattleOrder _orderNow;
        protected bool _timeoutReserved;

        public BattleBaseDirector director { get { return _directorMode.director;  } }
        public bool isRunningAction { get; protected set; }
        // 전투 종료 단계에 있는지 체크.
        public bool isInBattleEnd { get { return !VerifyNextOrder(BattleOrder.BattleEndRequest, false); } }    
        public virtual object battleResultData { get { return null; } }

        protected BattleCommunicator()
        {
            delayTimeUI = RequestHelper.settingsNow.responseDelayTimeInMulti;
        }

        #region 일반 메서드.
        protected void Initialize(DirectorMode directorMode)
        {
            _directorMode = directorMode;
            _orderNow = BattleOrder.None;

            OnCallInitialize();
        }

        public void Release()
        {
            OnCallRelease();
        }

        private List<int> ConvertIntList(List<Tile> tiles)
        {
            List<int> list = new List<int>();
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    list.Add(tiles[i].index);
                }
            }
            return list;
        }

        private Actor GetActorBySeq(int unitSeq)
        {
            var tileIndex = BattleSimulator.instance.GetTileIndex(unitSeq);
            return _directorMode.director.map.totalTiles[tileIndex].actor;
        }

        protected void SetNextOrder(BattleOrder nextOrder = BattleOrder.None)
        {
            if (nextOrder == BattleOrder.None)
            {
                _orderNow = (BattleOrder)((int)_orderNow + 1);
            }
            else
            {
                _orderNow = nextOrder;
            }

            // [181219][jhw] order의 상태는 notify에 따라 변화되지만 클라이언트는 여전히 액션중일 수 있으므로,
            // order변화시점에 action상태를 기록하고 실제로 turn 종료 요청 시점에 false로 설정하도록 처리. 
            if (_orderNow == BattleOrder.ActionRun)
            {
                isRunningAction = true;
            }
        }

        protected bool VerifyNextOrder(BattleOrder targetOrder, bool autoSet = true)
        {
            var valid = false;
            var orderNowInt = (int)_orderNow;
            var orderTargetInt = (int)targetOrder;
            if (_orderNow == BattleOrder.TurnEnd)
            {
                valid = (targetOrder == BattleOrder.TurnStartRequest);
            }
            valid = (valid) ? true : (orderNowInt < orderTargetInt);

            if (valid && autoSet)
            {
                SetNextOrder(targetOrder);
            }

            Debug.Log(string.Format(CTag.magenta("VerifyOrder:{0}, now:{1}, target:{2}"), valid, (BattleOrder)orderNowInt, targetOrder));

            return valid;
        }

        protected bool CanSendAction(int unitSeq)
        {            
            // [190213][jhw] 액션보내기는 ready action 단계에서만 전달가능하다. 
            if (_orderNow != BattleOrder.ReadyAction || !_directorMode.canOrderByMe)
            {
                Debug.Log(CTag.purple("CanSendAction false. " + _orderNow + ", " + _directorMode.canOrderByMe));
                return false;
            }

            var actor = GetActorBySeq(unitSeq);
            if (actor == null || actor.role.roleGroup != RoleGroup.Mercenary)
            {
                Debug.Log(CTag.purple("CanSendAction false. role group is NOT Mercenary"));
                return false;
            }

            return true;
        }

        // 턴 시작.
        protected void StartTurn(PTurnStart packet)
        {
            Debug.Log(CTag.purple("StartTurn userSeq : " + packet.userSeqNow));
            Debug.Log(CTag.purple("StartTurn useTurncountNow : " + packet.turnCountNow));

            _directorMode.SetControlSeq(packet.userSeqNow);
            OnCallStartTurn();
            
            if (director.turnController.isBonusTurn)
            {
                director.dataCollector.CollectTurnStart();
                director.turnController.SetPause(false);
            }
            else
            {
                director.StartTurn();
            }            
        }

        protected virtual void OnCallNotifyDisconnectUser(PNotifyUserNetworkStatus info) { }
        #endregion 일반 메서드.

        #region 전투 타입별(raid, pvp...) request or send.
        // 전투 시작 request.
        public void StartBattle()
        {
            if (!VerifyNextOrder(BattleOrder.BattleStartRequest))
            {
                // [181212][jhw] 유효한 order가 아니므로 무시한다.                
                return;
            }

            Debug.Log("BattleCommunicator.StartBattle");
            OnCallStartBattle();
        }

        // 전투 포기 request.
        public void GiveUpBattle()
        {            
            // [jhw] 일반적으로 battle end 시점에 포기를 누를수 있는 상황은 없고,
            // data mismatch시에 스샷을 찍기 위해 exit 처리를 하지 않은 상태에서 다시 포기를 눌러 스테이지를 나가려고 할 때 사용된다.
            if (_orderNow == BattleOrder.BattleEnd)
            {
                if (_directorMode.resultCause != BattleResultCause.None)
                {
                    BattleStage.instance.Exit();
                }

                return;
            }

            if(!VerifyNextOrder(BattleOrder.BattleEndRequest))
            {
                // [181212][jhw] 유효한 order가 아니므로 무시한다.
                return;
            }

            OnCallGiveUpBattle();
        }
        
        // 턴 시작 request.
        public void RequestTurnStart()
        {
            if (!VerifyNextOrder(BattleOrder.TurnStartRequest))
            {
                // [181212][jhw] 유효한 order가 아니므로 무시한다.                
                return;
            }

            isRunningAction = false;
            OnCallReadyTurn();
        }        

        // 턴 종료 request.
        public void EndTurn(int bonusFlag = 0)
        {
            isRunningAction = false;

            // action 종료 후에 이미 전투 종료 noti를 받은 상태라면 바로 전투종료 로직을 실행한다.
            if (_orderNow == BattleOrder.BattleEndResult)
            {
                RunBattleEnd();
                return;
            }

            if (!VerifyNextOrder(BattleOrder.TurnEndRequest))
            {
                // [181212][jhw] 유효한 order가 아니므로 무시한다.
                return;
            }

            OnCallEndTurn(bonusFlag);
        }

        protected void RunBattleEnd()
        {
            Debug.Log("BattleCommunicator.RunBattleEnd Start");

#if ENABLE_LOG
            // 데이터 미스매치가 난 경우, 오리지널 데이터를 상대에게 보내서 검증하도록 처리한다.
            if (_directorMode.resultCause == BattleResultCause.MisMatchData)
            {
                var data = _directorMode.GenerateVerifyData();
                RequestHelper.SendVerificationData(User.instance.seq, data);
            }
#endif
            _directorMode.StartBattleResult( ()=>{ SetNextOrder(BattleOrder.BattleEnd); } );
        }

        protected virtual void OnCallInitialize() { }
        protected abstract void OnCallRelease();
        protected abstract void OnCallStartBattle();
        protected abstract void OnCallGiveUpBattle();
        protected abstract void OnCallReadyTurn();
        protected virtual void OnCallStartTurn() { }
        protected abstract void OnCallEndTurn(int bonusFlag = 0);
        
        #endregion 전투 타입별(raid, pvp...) request or send.

        #region 전투 공용 request or send.        
        public void ReadyAction()
        {
            if (!VerifyNextOrder(BattleOrder.ReadyActionRequest))
            {
                // [181212][jhw] 유효한 order가 아니므로 무시한다.                
                return;
            }

            RequestHelper.ReadyAction(OnRequestReadyAction);
        }

        public void TimeoutAction(bool force = false)
        {
            // [190213][jhw] 타임아웃은 ready action 단계에서만 전달가능하다. 
            if (_orderNow != BattleOrder.ReadyAction)
            {
                return;
            }

            Debug.Log("TimeoutAction user : " + User.instance.seq);

            if (!force && _directorMode.isMyControl && TouchSensor.instance.isOnTimer)
            {   
                TouchSensor.instance.SetTimeOver();
            }
            else
            {   
                SetNextOrder(BattleOrder.TimeoutRequest);
                RequestHelper.TimeoutAction(OnRequestTimeoutAction);
            }
        }

        public bool RunAction(int unitSeq, long skillId, List<Tile> tiles)
        {
            Debug.Log(CTag.purple("RunAction : " + unitSeq + ", " + skillId + ", " + tiles.Count));

            if (!CanSendAction(unitSeq))
            {   
                return false;
            }

            SetNextOrder(BattleOrder.ActionRun);
            
            BattleStage.instance.myTurnTimeIndex = 0;

            var list = ConvertIntList(tiles);
            RequestHelper.RunAction(User.instance.seq, unitSeq, skillId, Json.Serialize(list));

            return true;
        }

        public void SelectTile(int unitSeq, List<Tile> tiles)
        {
            Debug.Log(CTag.purple("SelectTile : " + unitSeq + ", " + tiles.Count + ", " + _directorMode.director.actActor));

            if (!CanSendAction(unitSeq))
            {
                return;
            }

            var list = ConvertIntList(tiles);
            RequestHelper.SendSelectTile(User.instance.seq, unitSeq, Json.Serialize(list));
        }

        public void ReadySkill(int unitSeq, long skillId, bool isOn)
        {
            Debug.Log(CTag.purple("ReadySkill : " + unitSeq + ", " + skillId + ", " + isOn));

            if (!CanSendAction(unitSeq))
            {
                return;
            }

            RequestHelper.SendReadySkill(User.instance.seq, unitSeq, skillId, isOn);
        }        
        #endregion 전투 공용 request or send.

        //================================================================================
        // 콜백이나 delegate, 이벤트 처리 메서드./
        //================================================================================
        #region 전투 공용 response.
        // 기본적인 응답처리 처리가 필요한 경우에 호출. 
        protected bool CheckValidResponse(RequestResult result, ref bool errorHandling, BattleOrder battleOrder, bool setNextOrder)
        {
            // 유효하지 않는 order로 왔을 경우, error자체를 무시해야 하므로 order 검색을 먼저 한다.
            if (setNextOrder)
            {
                if (!VerifyNextOrder(battleOrder))
                {
                    Debug.Log(CTag.purple("CheckValidResponse Invalid Order#1: "));
                    // [181212][jhw] 유효한 order가 아니므로 request를 무시한다.   
                    errorHandling = true;
                    return false;
                }
            }
            else
            {
                if (_orderNow != battleOrder)
                {
                    Debug.Log(CTag.purple("CheckValidResponse Invalid Order#2: "));
                    // [181212][jhw] 유효한 order가 아니므로 response를 무시한다.    
                    errorHandling = true;
                    return false;
                }
            }            
            
            if (!result.isSuccess)
            {
                Debug.Log(CTag.purple("CheckValidResponse request failed: "));
                return false;
            }
            
            return true;
        }

        // ready action request의 응답 콜백.
        private void OnRequestReadyAction(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnRequestReadyAction"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.ReadyActionRequest, false))
            {
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, true, false, delayTimeUI);
        }

        // timeout request의 응답 콜백.
        protected void OnRequestTimeoutAction(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnRequestTimeoutAction"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.ActionWait, true))
            {
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, true, false, delayTimeUI);
        }
        #endregion 전투 공용 response.

        #region 전투 공용 notify.
        //================ IBattleCommonListener ==================//        
        public void OnReadyAction(PNotifyReadyAction info)
        {
            Debug.Log(CTag.purple("OnReadyAction"));

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);

            if (!VerifyNextOrder(BattleOrder.ReadyAction))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            var passedTime = ServerTime.UtcNow - info.startTime;
            var passedSecond = (float)passedTime.TotalSeconds;
            if (passedSecond < 0)
            {
                passedSecond = 0;
            }

            BattleStage.instance.remainTimeDelta = passedSecond;
            BattleStage.instance.battleStartTime = info.startTime;

            director.StartAction();
        }

        public void OnRunAction(PNotifyRunAction info)
        {
            Debug.Log(CTag.purple("OnRunAction"));

            if (!VerifyNextOrder(BattleOrder.ActionRun))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);

            BattleStage.instance.otherTurnTimeIndex = 0;
            var tileIndex = BattleSimulator.instance.GetTileIndex(info.useMercenarySeq);
            var actor = _directorMode.director.map.totalTiles[tileIndex].actor;
            
            var skill = actor.role.GetSkill(info.useSkillId);            
            if (skill != null)
            {
                // 액티브 스킬 사용.
                Debug.Log(CTag.purple("OnRunAction Skill"));

                BattleStage.instance.director.SetUseSkill(info.useMercenarySeq, (int)info.useSkillId, info.actionTileList[0].index);

                EventMessenger.Send(EventName.btMainUnitAction, this, null);                
            }
            else if (actor.role.attackBehavior.dbId == info.useSkillId)
            {
                // 일반 공격 실행.
                Debug.Log(CTag.purple("OnRunAction Attack"));

                // [190327][jhw] 이 경우는 네트워크 상태가 좋지 않은경우, actor설정 변경이 전달되지 않을 수 있으므로,
                // 항상 마지막에 최종 actor를 설정하도록 코드를 추가.
                if (director.actActor == null || !director.actActor.Equals(actor))
                {
                    director.SetActionActor(actor);
                    _directorMode.ChangeMainMercenary(actor);
                }

                // 일반 공격일 경우, 메인 유닛 owner로 등록.
                director.turnController.mainUnitPhase.SetOwner(actor);
                
                // touch 관련 기능 hide.
                EventMessenger.Send(EventName.hideTouchTime, this);
                director.HideGuide(info.useMercenarySeq);

                // 타겟팅 처리.
                actor.role.targetingBehavior.SetTargetList(info.actionTileList);

                EventMessenger.Send(EventName.btMainUnitAction, this, null);
            }
        }

        public void OnSelectTile(PNotifySelectTile info)
        {
            Debug.Log(CTag.purple("OnSelectTile : " + _directorMode.director.actActor + ", " + info.selectTileList.Count));

            // [181212][jhw] 전투 종료 단계에 있으므로 notify를 무시한다.
            if (isInBattleEnd)
            {                                        
                return;
            }
            
            if (info.selectTileList.Count > 0)
            {
                // action actor 등록.
                var tileIndex = BattleSimulator.instance.GetTileIndex(info.useMercenarySeq);
                var actor = director.map.totalTiles[tileIndex].actor;

                if(director.SetActionMercenary(actor))
                {
                    actor.ShowTargetingGuide(info.selectTileList);
                }
            }
            else
            {   
                director.CancelTargeting();
            }
        }

        public void OnReadySkill(PNotifyActiveReady info)
        {
            Debug.Log(CTag.purple("OnReadySkill"));

            // [181212][jhw] 전투 종료 단계에 있으므로 notify를 무시한다.
            if (isInBattleEnd)
            {
                return;
            }

            _directorMode.director.SetUseSkillTargeting(info.useMercenarySeq, info.useSkillId, info.isEnable);
        }

        public void OnEmoticon(PNotifyEmoticon info)
        {
            Debug.Log(CTag.purple("OnEmoticon"));

            // [181212][jhw] 전투 종료 단계에 있으므로 notify를 무시한다.
            if (isInBattleEnd)
            {
                return;
            }

            EventMessenger.Send(EventName.showEmotion, this, info);
        }

        public void OnTimeoutAction()
        {
            Debug.Log(CTag.purple("OnTimoutAction"));

            if (!VerifyNextOrder(BattleOrder.ActionRun))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);

            // action 준비중인 상태였다면 time out이 발생했으므로 행동을 cancel시킨다.            
            director.CancelActionReady();

            if (_directorMode.isMyControl)
            {
                BattleStage.instance.myTurnTimeIndex++;
            }
            else
            {
                BattleStage.instance.otherTurnTimeIndex++;
            }

            // 메인 유닛 턴 종료.
            _directorMode.director.turnController.PassMainUnitPhase();
        }

        public void OnEndTurn()
        {
            Debug.Log(CTag.purple("OnEndTurn"));

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);

            if (!VerifyNextOrder(BattleOrder.TurnEnd))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            // 다음 턴 시작.
            _directorMode.GoNextTurn();
        }

        public void OnDisconnectUser(PNotifyUserNetworkStatus info)
        {
            Debug.Log(CTag.purple("OnDisconnectUser : " + info.userSeqTarget));

            // [181212][jhw] 전투 종료 단계에 있으므로 notify를 무시한다.
            if (isInBattleEnd)
            {
                return;
            }

            OnCallNotifyDisconnectUser(info);
        }

        public void OnReconnectUser(PNotifyUserNetworkStatus info)
        {
            Debug.Log(CTag.purple("OnReconnectUser : " + info.userSeqTarget));

            // [181212][jhw] 전투 종료 단계에 있으므로 notify를 무시한다.
            if (isInBattleEnd)
            {
                return;
            }
        }

        public void OnVerifyData(PNotifyDataVerify info)
        {
            Debug.Log(CTag.purple("OnVerifyData"));

            _directorMode.VerifyStageData(info.senderSeq, info.verifyData);

#if UNITY_EDITOR
            Debug.Break();
#endif
        }
        //================ IBattleCommonListener ==================//
        #endregion 전투 공용 notify.
    }
}