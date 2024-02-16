using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;


namespace Eureka
{
    //  pvp에서 서버 통신을 담당하는 클래스.
    public sealed class PvPCommunicator : BattleCommunicator, IPVPBattleListener
    {
        private PNotifyPvpEnd _pvpEndInfo;

        public override object battleResultData { get { return _pvpEndInfo; } }


        private PvPCommunicator() : base() { }
        public static PvPCommunicator FromDirector(DirectorMode directorMode)
        {
            var com = new PvPCommunicator();
            com.Initialize(directorMode);
            RequestHelper.SetListener(com);

            return com;
        }

        // 클래스 제거.
        protected override void OnCallRelease()
        {
            RequestHelper.RemoveListener(this);
        }

        // 전투 시작 request
        protected override void OnCallStartBattle()
        {            
            RequestHelper.PvpStart(OnPvpStart);
        }

        // 전투 포기 request
        protected override void OnCallGiveUpBattle()
        {
            RequestHelper.PvpGiveUp(OnPvpGiveUp);
        }

        // 턴 실행 준비 완료 request.
        protected override void OnCallReadyTurn()
        {
            Debug.Log(CTag.purple("PvP. OnCallReadyTurn"));
            
            var director = _directorMode.director;
            var useTurn = director.turnLimit - director.currentTurn;
            
            RequestHelper.PvpTurnReady(useTurn, OnPvpTurnReady);
        }

        // 턴 종료 알림 request.
        protected override void OnCallEndTurn(int bonusFlag = 0)
        {
            Debug.Log(CTag.purple("EndTurn"));

            var director = _directorMode.director;
            var useTurn = director.turnLimit - director.currentTurn;            
            var myScore = BattleStage.instance.myGameScore;
            var otherScore = BattleStage.instance.otherGameScore;
            var dataHash = _directorMode.CalculateHashTurn();

            useTurn = (bonusFlag > 0) ? useTurn + 1 : useTurn;  
            RequestHelper.PvpTurnEnd(useTurn, myScore, otherScore, bonusFlag, dataHash,
                                     _directorMode.GetTotalScore(BattleStage.instance.homeUserSeq),
                                     _directorMode.GetTotalScore(BattleStage.instance.awayUserSeq),
                                     OnPvpTurnEnd);
        }

        //================================================================================
        // 콜백이나 delegate, 이벤트 처리 메서드./
        //================================================================================
#region pvp 전용 reqeust의 응답콜백.
        //================ 각 request의 call back ==================//
        // pvp 전투 시작 reqeust 콜백.
        private void OnPvpStart(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnPvpStart"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.BattleStartRequest, false))
            {
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, true, false, delayTimeUI);
        }

        // pvp 전투 포기 reqeust 콜백.
        private void OnPvpGiveUp(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnPvpGiveUp"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.BattleEndRequest, false))
            {
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingServer, true, false, delayTimeUI);
        }

        // pvp 턴 준비완료 reqeust 콜백.
        private void OnPvpTurnReady(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnPvpTurnReady"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.TurnStart, true))
            {
                return;
            }

            StartTurn(result.data as PTurnStart);
        }

        // pvp 턴 종료 체크 reqeust 콜백.
        private void OnPvpTurnEnd(RequestResult result, ref bool errorHandling)
        {
            Debug.Log(CTag.purple("OnPvpTurnEnd"));

            if (!CheckValidResponse(result, ref errorHandling, BattleOrder.TurnEndRequest, false))
            {
                return;
            }

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, true, false, delayTimeUI);
        }
        //================ 각 request의 call back ==================//
#endregion pvp 전용 reqeust의 응답콜백.

 #region pvp 전용 notify.
        //================ IPVPBattleListener ==================//
        // pvp 전투 시작. 초기 시작시, 한 번 호출 됨.
        public void OnStartPvP()
        {
            Debug.Log(CTag.purple("Notify OnStartPvP"));

            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);

            if (!VerifyNextOrder(BattleOrder.BattleStart))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            UIManager.instance.OpenView(UIViewId.ui_view_battle_pvp_start);
        }

        public void OnEndPvP(PNotifyPvpEnd info)
        {
            Debug.Log(CTag.purple("Notify OnEndPvP"));

            // 유저 대기 상태 혹은 서버 대기 상태 두 군데에서 noti가 올 수 있으므로 둘 다 hide 시킨다.
            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingServer, false);
            UIPopupRequestWaiting.Count(RequestWaitingName.multiWaitingUser, false);
            
            if (!VerifyNextOrder(BattleOrder.BattleEndResult))
            {
                // [181212][jhw] 유효한 order가 아니므로 notify를 무시한다.                
                return;
            }

            _pvpEndInfo = info;
            _directorMode.SetBattleResult(info.pvo.result, info.pvo.cause);            

            // 액션 실행중에는 end를 받아도 무시하고 나중에 turn 종료시에 처리하도록 한다.
            if(!isRunningAction)
            {                
                RunBattleEnd();
            }
        }
        //================ IPVPBattleListener ==================//
#endregion pvp 전용 notify.
    }
}