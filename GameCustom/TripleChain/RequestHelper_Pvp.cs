using UnityEngine;
using System;
using System.Collections.Generic;


namespace Eureka
{
    // pvp 매칭 전용 notify 리스너.
    public interface IPVPMatchingListener 
    {
        void OnSuccessMatching(PPvpMatchingInfo info);    // 매칭 성공.
        void OnFailedMatching(int result);    // 매칭 실패(타임아웃, pvp서버문제등)
        void OnPrepare(PPvpPrepareInfo info); // pvp 상대방 데이터 전송 및 준비.
        void OnStartStage(int result); // stage load 시작.
    }

    // pvp 전투 전용 notify 리스너.
    public interface IPVPBattleListener : IBattleCommonListener
    {
        void OnStartPvP();
        void OnEndPvP(PNotifyPvpEnd info);
    }

    //================================================================================
    // pvp용 request helper partial 클래스.
    //================================================================================
    public static partial class RequestHelper
    {
        private static IPVPMatchingListener _listenerPvpMatching;
        private static IPVPBattleListener _listenerPvpBattle;

        #region pvp 매칭 리스너.
        // pvp 매칭 notify 리스너 설정.
        public static void SetListener(IPVPMatchingListener listener)
        {
            if (listener == null)
            {
                return;
            }

            _listenerPvpMatching = listener;
            var netManager = NetworkManager.instance;
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_MATCHING, OnNotifyPvpMatchingSuccess);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_AUTOCANCEL, OnNotifyPvpMatchingFail);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_PREPARE, OnNotifyPvpPrepare);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_STAGE_START, OnNotifyStageStart);

            netManager.AddCallBackMultiple(PACKET_TYPE.N_FRIEND_PVP_INVITE_RESULT, OnNotifyFriendPvpInviteResult);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_FRIEND_PVP_PREPARE, OnNotifyFriendPvpPrepare);
        }
        // pvp 매칭 notify 리스너 체크 해제.
        public static void RemoveListener(IPVPMatchingListener listener)
        {
            if (listener == null)
            {
                return;
            }

            if (listener.Equals(_listenerPvpMatching))
            {
                ClearPVPMatchingListener();
            }
        }
        // pvp 매칭 notify 리스너 강제 해제.
        public static void ClearPVPMatchingListener()
        {
            _listenerPvpMatching = null;
            var netManager = NetworkManager.instance;
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_MATCHING);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_AUTOCANCEL);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_PREPARE);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_STAGE_START);
        }
        #endregion pvp 매칭 리스너.

        #region pvp 전투 리스너.
        // pvp 전투 notify 리스너 설정.
        public static void SetListener(IPVPBattleListener listener)
        {
            if (listener == null)
            {
                return;
            }

            // IPVPBattleListener 부모인 IBattleCommonListener는 공용메서드로 같이 사용되므로 추가 설정을 해준다.
            SetListener(listener as IBattleCommonListener);

            _listenerPvpBattle = listener;
            var netManager = NetworkManager.instance;
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_START, OnNotifyPvpStart);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_PVP_END, OnNotifyPvpEnd);

            netManager.AddCallBackMultiple(PACKET_TYPE.N_FRIEND_PVP_END, OnNotifyFriendPvpEnd);
        }
        // pvp 전투 notify 리스너 체크 해제.
        public static void RemoveListener(IPVPBattleListener listener)
        {
            if (listener == null)
            {
                return;
            }

            if (listener.Equals(_listenerPvpBattle))
            {
                ClearPVPBattleListener();
            }
        }
        // pvp 전투 notify 리스너 강제 해제.
        public static void ClearPVPBattleListener()
        {
            // IPVPBattleListener 부모인 IBattleCommonListener는 공용메서드로 같이 사용되었으므로 추가 해제를 해준다.
            RemoveListener(_listenerPvpBattle as IBattleCommonListener);

            _listenerPvpBattle = null;
            var netManager = NetworkManager.instance;
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_START);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_PVP_END);

            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_FRIEND_PVP_END);
        }
        #endregion pvp 전투 리스너.

        #region pvp 로비 정보 reqeust.
        // pvp menu enter
        public static bool PvpInfo(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpInfo");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_INFO;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            _client.SendPacket(writer, OnPvpInfo);

            return true;
        }
        public static void OnPvpInfo(PacketReader reader)
        {
            Debug.Log("OnPvpInfo");

            var packet = new PPvpInfo(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_INFO, result);
        }

        //pvp all ranking
        public static bool PvpRanking(int seasonNumber, int pageNum, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpRanking");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_RANKING;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write(seasonNumber);
            writer.Write(pageNum);
            _client.SendPacket(writer, OnPvpRanking);

            return true;
        }
        public static void OnPvpRanking(PacketReader reader)
        {
            Debug.Log("OnPvpRanking");

            var packet = new PPvpRanking(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_RANKING, result);
        }

        //pvp halls of frame
        public static bool PvpHallOfFame(int pageNum, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpHallOfFame");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_HALLOFFAME;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write(pageNum);
            _client.SendPacket(writer, OnPvpHallOfFame);

            return true;
        }
        public static void OnPvpHallOfFame(PacketReader reader)
        {
            Debug.Log("OnPvpHallOfFame");

            var packet = new PPvpHOF(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_HALLOFFAME, result);
        }

        //pvp friend ranking
        public static bool PvpFriendRanking(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpFriendRanking");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_FRIEND_RANKING;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            _client.SendPacket(writer, OnPvpFriendRanking);

            return true;
        }
        public static void OnPvpFriendRanking(PacketReader reader)
        {
            Debug.Log("OnPvpFriendRanking");

            var packet = new PPvpFriendRanking(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_FRIEND_RANKING, result);
        }

        //pvp my ranking
        public static bool PvpMyRank(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpMyRank");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_MYRANK;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            _client.SendPacket(writer, OnPvpMyRank);

            return true;
        }
        public static void OnPvpMyRank(PacketReader reader)
        {
            Debug.Log("OnPvpMyRank");

            var packet = new PPvpMyRank(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_MYRANK, result);
        }

        //pvp previous season
        public static bool PvpRecords(int pageNum, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpRecords");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_RECORDS;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write(pageNum);
            _client.SendPacket(writer, OnPvpRecords);

            return true;
        }
        public static void OnPvpRecords(PacketReader reader)
        {
            Debug.Log("OnPvpRecords");

            var packet = new PPvpRecords(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_RECORDS, result);
        }

        //pvp current season match
        public static bool PvpHistory(int pageNum, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpHistory");

            PACKET_TYPE packetType = PACKET_TYPE.R_PVP_HISTORY;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write(pageNum);
            _client.SendPacket(writer, OnPvpHistory);

            return true;
        }
        public static void OnPvpHistory(PacketReader reader)
        {
            Debug.Log("OnPvpHistory");

            var packet = new PPvpHistory(reader);
            var result = new RequestResult(packet);

            FinishRequest(PACKET_TYPE.R_PVP_HISTORY, result);
        }
        #endregion pvp 로비 정보 reqeust.

        #region pvp 매칭 request.
        // pvp 등록.
        public static bool PvpRegist(List<int> seqList, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpRegist");

            var packetType = PACKET_TYPE.R_PVP_REGIST;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            for (int i = 0, count = seqList.Count; i < count; i++)
            {
                writer.Write(seqList[i]);
            }
            _client.SendPacket(writer, OnPvpRegist);

            return true;
        }
        private static void OnPvpRegist(PacketReader reader)
        {
            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }

        // pvp 등록 해제.
        public static bool PvpUnregist(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpUnregist");

            return RequestSimple(PACKET_TYPE.R_PVP_UNREGIST, OnPvpUnregist, cbFinish);
        }
        private static void OnPvpUnregist(PacketReader reader)
        {
            Debug.Log("OnPvpUnregist");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            if (packet.isSuccess)
            {
                NetworkManager.instance.RemoveCallBackOneShot(PACKET_TYPE.N_PVP_MATCHING);
                NetworkManager.instance.RemoveCallBackOneShot(PACKET_TYPE.N_PVP_PREPARE);
            }

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }

        // pvp 스테이지 로드 준비 완료 request.
        public static bool PvpStageStart(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpStageStart");

            return RequestSimple(PACKET_TYPE.R_PVP_STAGE_START, OnPvpStageStart, cbFinish);
        }
        private static void OnPvpStageStart(PacketReader reader)
        {
            Debug.Log("OnPvpStageStart");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);  

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }
        #endregion pvp 매칭 request.

        #region pvp 매칭 notify.
        // 매칭 성공시.
        private static void OnNotifyPvpMatchingSuccess(PacketReader reader)
        {
            Debug.Log("OnNotifyPvpMatchingSuccess");

            var info = new PPvpMatchingInfo(reader);
            _listenerPvpMatching?.OnSuccessMatching(info);
        }

        // 매칭 실패시.
        private static void OnNotifyPvpMatchingFail(PacketReader reader)
        {
            Debug.Log("OnNotifyPvpMatchingFail");

            var result = reader.ReadInt32();
            _listenerPvpMatching?.OnFailedMatching(result);
        }

        // 매칭된 상대의 정보 전달 및 준비.
        private static void OnNotifyPvpPrepare(PacketReader reader)
        {
            Debug.Log("OnNotifyPvpPrepare");

            var info = new PPvpPrepareInfo(reader);
            User.instance.currentOtherParty.Clear();
            for (int i = 0; i < SystemConfig.partyCountMax; i++)
            {
                if (info.mercenaryPVOList.Count <= i)
                {
                    User.instance.currentOtherParty.Add(null);
                    continue;
                }

                var mercenaryPVO = info.mercenaryPVOList[i];
                var unit = Unit.FromId(mercenaryPVO.mercenary_id);
                unit.seq = mercenaryPVO.mercenary_seq;
                unit.SetGrade(mercenaryPVO.grade, mercenaryPVO.level);

                for (int j = 0, count = info.itemPVOList.Count; j < count; j++)
                {
                    var itemPVO = info.itemPVOList[j];
                    if (itemPVO.equipMercenarySeq == mercenaryPVO.mercenary_seq)
                    {
                        var item = Item.FromPVO(itemPVO);
                        var slot = item.slot;
                        if (item.category == ItemCategory.Rune)
                        {
                            var rune1Int = (int)EquipmentSlot.Rune1;
                            var runeMaxInt = rune1Int + SystemConfig.runeSlotMax;
                            for (int k = rune1Int; k < runeMaxInt; k++)
                            {
                                var tempSlot = (EquipmentSlot)k;
                                if (unit.GetEquipment(tempSlot) == null)
                                {
                                    slot = tempSlot;
                                    break;
                                }
                            }
                        }

                        unit.SetEquipment(item, slot);
                    }
                }
                if (mercenaryPVO.costume_id != 0)
                {
                    var costume = Item.FromId(mercenaryPVO.costume_id);
                    unit.SetEquipment(costume, EquipmentSlot.Costume);
                }
                unit.SetGradeSkill(mercenaryPVO.gradeSkill);
                unit.CreateAllSkillsImpl(new UnitSkillBaseCVO(mercenaryPVO));
                unit.SetPvpOtherMercenary(BattleStage.instance.otherUserSeq);

                User.instance.currentOtherParty.Add(unit);
            }

            _listenerPvpMatching?.OnPrepare(info);
        }

        // pvp 스테이지 로드 시작 notify. 결과에 따라 성공, 실패가 결정 됨.
        private static void OnNotifyStageStart(PacketReader reader)
        {
            Debug.Log("OnNotifyStageStart");

            var result = reader.ReadInt32();
            _listenerPvpMatching?.OnStartStage(result);

            // 0이면 시작가능한 상태이므로 리스너 삭제.
            if (result == 0)
            {
                ClearPVPMatchingListener();
            }
        }
        #endregion pvp 매칭 notify.

        #region pvp 전투 request.        
        // 전투 준비 완료 request.
        public static bool PvpStart(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpStart");

            return RequestSimple(PACKET_TYPE.R_PVP_START, OnPvpStart, cbFinish);
        }
        private static void OnPvpStart(PacketReader reader)
        {
            Debug.Log("OnPvpStart");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }

        // 전투 포기 request.
        public static bool PvpGiveUp(OnFinishRequest cbFinish)
        {
            Debug.Log("PvpGiveup");

            return RequestSimple(PACKET_TYPE.R_PVP_GIVEUP, OnPvpGiveUp, cbFinish);
        }
        private static void OnPvpGiveUp(PacketReader reader)
        {
            Debug.Log("OnPvpGiveup");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        } 

        // 턴 시작 request.
        public static bool PvpTurnReady(int usedTurn, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpTurnReady");

            var packetType = PACKET_TYPE.R_PVP_TURN_START;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            Debug.Log(string.Format("PvpTurnReady. usedTurn : {0}", usedTurn));

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write((byte)usedTurn);
            _client.SendPacket(writer, OnPvpTurnReady, settingsNow.responseDelayTimeInMulti);

            return true;
        }
        private static void OnPvpTurnReady(PacketReader reader)
        {
            Debug.Log("OnPvpTurnReady");

            var packet = new PTurnStart(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }

        // 턴 종료 request.
        public static bool PvpTurnEnd(int usedTurn, int myUnitCount, int otherUnitCount, int bonusFlag,
            string stageDataHash, int homeUserScore, int otherUserScore, OnFinishRequest cbFinish)
        {
            Debug.Log("PvpTurnEnd");

            Debug.Log(string.Format("PvpTurnEnd. usedTurn : {0}, myScore : {1}, otherScore : {2}, bonusFlag : {3}",
                usedTurn, myUnitCount, otherUnitCount, bonusFlag));

            var packetType = PACKET_TYPE.R_PVP_TURN_END;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write((byte)usedTurn);
            writer.Write((byte)myUnitCount);
            writer.Write((byte)otherUnitCount);
            writer.Write((byte)bonusFlag);
            writer.WriteASCII(stageDataHash);
            writer.Write((ushort)homeUserScore);
            writer.Write((ushort)otherUserScore);

            _client.SendPacket(writer, OnPvpTurnEnd, settingsNow.responseDelayTimeInMulti);

            return true;
        }
        private static void OnPvpTurnEnd(PacketReader reader)
        {
            Debug.Log("OnPvpTurnEnd");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }
        #endregion pvp 전투 request.

        #region pvp 전투 notify.
        // pvp 전투 시작 notify.
        private static void OnNotifyPvpStart(PacketReader reader)
        {
            Debug.Log("OnNotifyPvpStart");

            _listenerPvpBattle?.OnStartPvP();
        }
        // pvp 전투 종료 notify.
        private static void OnNotifyPvpEnd(PacketReader reader)
        {
            var info = new PNotifyPvpEnd(reader, BattleResultPvpStageType.Ranking);

            Debug.Log(string.Format("OnNotifyPvpEnd. cause : {0}, result : {1}", info.pvo.cause, info.pvo.result));

            _listenerPvpBattle?.OnEndPvP(info);
        }
        #endregion pvp 전투 notify.
    }

    //================================================================================
    // pvp의 request, notify packet 생성 클래스.
    //================================================================================
    #region pvp response, notify packet 정의.    
    #endregion pvp response, notify packet 정의.
}