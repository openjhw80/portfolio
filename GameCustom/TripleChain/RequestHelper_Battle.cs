using UnityEngine;
using System;
using System.Collections.Generic;
using LitJson;


namespace Eureka
{
    // 전투에서 사용하는 공용 notify 리스너.
    public interface IBattleCommonListener
    {
        void OnReadyAction(PNotifyReadyAction info);            // 턴 action 시작을 알리는 서버의 notify.
        void OnSelectTile(PNotifySelectTile info);              // 타일 선택 정보 packet 받기.
        void OnReadySkill(PNotifyActiveReady info);             // 액티브 스킬 사용 준비 정보 받기.
        void OnEmoticon(PNotifyEmoticon info);                  // 이모티콘 사용 정보 받기.                
        void OnRunAction(PNotifyRunAction info);                // 다른 유저의 액션이 실행되었음을 알리는 notify.
        void OnTimeoutAction();                                 // turn 실행시간 종료 처리 notify.
        void OnEndTurn();   // turn이 종료에 대한 결과가 처리되었음을 알리는 notify.
        void OnDisconnectUser(PNotifyUserNetworkStatus info);
        void OnReconnectUser(PNotifyUserNetworkStatus info);        
        void OnVerifyData(PNotifyDataVerify info);          // 검증을 위한 오리지널 데이터 받기 notify.
    }

    //================================================================================
    // 전투 공용 request helper partial 클래스.
    //================================================================================
    public static partial class RequestHelper
    {
        private static IBattleCommonListener _listenerBattleCommon;

        #region 전투 공용 notify 리스너.
        // 전투 공용 notify 리스너 설정.
        public static void SetListener(IBattleCommonListener listener)
        {
            if (listener == null)
            {
                return;
            }

            _listenerBattleCommon = listener;
            var netManager = NetworkManager.instance;
            netManager.AddCallBackMultiple(PACKET_TYPE.N_READY_ACTION, OnNotifyReadyAction);
            netManager.AddCallBackMultiple(PACKET_TYPE.P_SELECT_TILE, OnNotifySelectTile);
            netManager.AddCallBackMultiple(PACKET_TYPE.P_ACTIVE_READY, OnNotifyReadySkill);
            netManager.AddCallBackMultiple(PACKET_TYPE.P_EMOTICON, OnNotifyEmoticon);
            netManager.AddCallBackMultiple(PACKET_TYPE.P_ACTION_OR_SKILL, OnNotifyRunAction);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_ACTION_TIMEOUT, OnNotifyTimeoutAction);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_TURN_END, OnNotifyEndTurn);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_USER_DISCONNECT, OnNotifyDisconnectUser);
            netManager.AddCallBackMultiple(PACKET_TYPE.N_USER_RECONNECT, OnNotifyReconnectUser);
            netManager.AddCallBackMultiple(PACKET_TYPE.P_DATA_VERIFY, OnNotifyVerifyData);
        }
        // 전투 공용 notify 리스너 체크 해제.
        public static void RemoveListener(IBattleCommonListener listener)
        {
            if (listener == null)
            {
                return;
            }

            if (listener.Equals(_listenerBattleCommon))
            {
                ClearBattleCommonListener();
            }
        }
        // 전투 공용 notify 리스너 강제 해제.
        public static void ClearBattleCommonListener()
        {
            _listenerBattleCommon = null;
            var netManager = NetworkManager.instance;
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_READY_ACTION);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.P_SELECT_TILE);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.P_ACTIVE_READY);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.P_EMOTICON);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.P_ACTION_OR_SKILL);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_ACTION_TIMEOUT);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_TURN_END);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_USER_DISCONNECT);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.N_USER_RECONNECT);
            netManager.RemoveCallBackMultiple(PACKET_TYPE.P_DATA_VERIFY);
        }
        #endregion 전투 공용 notify 리스너.

        #region 전투 공용 request.
        // play data 전송.
        public static bool SendTurnPlayData(TurnCollection collection, byte[] compressed)
        {
            Debug.Log("RequestHelper_Battle.SendTurnPlayData Start");

            if(!CanRequest())
            {
                return false;
            }

            var packetType = PACKET_TYPE.R_DETAIL_GAME_PLAY_TURN_INFO;
            if (!AddCallBack(packetType, null))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            writer.Write((byte)collection.stageType);
            writer.Write((byte)collection.continued);
            writer.Write((byte)collection.stagePhase);
            writer.Write(collection.userSeq);
            writer.Write(collection.isAutoToByte);
            writer.Write((byte)collection.turnBonus);
            writer.Write(collection.turnNowToByte);
            writer.Write(collection.turnUsedToByte);
            writer.Write(collection.turnUsedAllToByte);
            writer.Write(collection.turnTime);
            writer.Write((short)collection.mpBefore);
            writer.Write((short)collection.mpAfter);
            writer.Write((short)collection.mpUsed);
            writer.Write((short)collection.mpGain);
            writer.Write((int)collection.mainMercenaryId);
            writer.Write((byte)collection.mainMercenaryIndex);
            writer.Write(collection.damageTo.sumToInt);
            writer.Write(collection.damageTo.max);
            writer.Write(collection.healTo.sumToInt);
            writer.Write(collection.healTo.max);
            writer.Write(collection.damageFrom.sumToInt);
            writer.Write(collection.damageFrom.max);
            writer.Write(collection.healFrom.sumToInt);
            writer.Write(collection.healFrom.max);
            // json 압축 문자열 추가.
            writer.Write((short)compressed.Length);
            writer.Write(compressed);

            _client.SendPacket(writer, OnSendTurnPlayData);

            return true;
        }
        // 데이터 전송 확인을 위한 임시 callback.
        private static void OnSendTurnPlayData(PacketReader reader)
        {
#if ENABLE_LOG
            var packet = new PDefaultResponse(reader);
            Debug.LogWarning("OnSendTurnPlayData isSuccess : " + packet.isSuccess);
#endif
        }

        // action ready request.
        public static bool ReadyAction(OnFinishRequest cbFinish)
        {
            Debug.Log("ReadyAction");

            var packetType = PACKET_TYPE.R_READY_ACTION;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);
            _client.SendPacket(writer, OnReadyAction, settingsNow.responseDelayTimeInMulti);

            return true;
        }
        private static void OnReadyAction(PacketReader reader)
        {
            Debug.Log("OnReadyAction");

            var packet = new PDefaultResponse(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }

        // 턴 제한 시간 timeout request.
        public static bool TimeoutAction(OnFinishRequest cbFinish)
        {
            Debug.Log("TimeoutAction");

            var packetType = PACKET_TYPE.R_ACTION_TIMEOUT;
            if (!AddCallBack(packetType, cbFinish))
            {
                return false;
            }

            var writer = _client.PreparePacket((ushort)packetType);            
            _client.SendPacket(writer, OnTimeoutAction, settingsNow.responseDelayTimeInMulti);

            return true;
        }
        private static void OnTimeoutAction(PacketReader reader)
        {
            Debug.Log("OnTimeoutAction");

            var packet = new PTimeoutAction(reader);
            var result = new RequestResult(packet);

            FinishRequest((PACKET_TYPE)reader.requestPacketType, result);
        }
        #endregion 전투 공용 request.

        #region 전투 공용 단방향 send.
        public static void RunAction(long userSeq, int unitSeq, long skillId, string tilesJson)                               
        {
            Debug.Log(Utility.Format("RunAction userSeq:{0}, unitSeq:{1}, skillId:{2}, tilesJson:{3}"
                , userSeq, unitSeq, skillId, tilesJson));

            var writer = _client.PreparePacket((ushort)PACKET_TYPE.P_ACTION_OR_SKILL);
            writer.Write(userSeq);
            writer.Write(unitSeq);
            writer.Write((int)skillId);
            writer.WriteUnicode(tilesJson);
            _client.SendPacket(writer, null);
        }

        // 선택된 타일 리스트 정보 보내기.
        public static void SendSelectTile(long userSeq, int unitSeq, string tilesJson)
        {
            Debug.Log(Utility.Format("SendSelectTile userSeq:{0}, unitSeq:{1}, ,tilesJson:{2}"
                , userSeq, unitSeq, tilesJson));

            var writer = _client.PreparePacket((ushort)PACKET_TYPE.P_SELECT_TILE);
            writer.Write(userSeq);
            writer.Write(unitSeq);
            writer.WriteUnicode(tilesJson);
            _client.SendPacket(writer, null);
        }

        // 액티브 스킬 준비 상태 보내기.
        public static void SendReadySkill(long userSeq, int unitSeq, long skillId, bool isOn)
        {
            Debug.Log(Utility.Format("SendReadySkill userSeq:{0}, unitSeq:{1}, skillId:{2}, isOn:{3}"
                , userSeq, unitSeq, skillId, isOn));

            var writer = _client.PreparePacket((ushort)PACKET_TYPE.P_ACTIVE_READY);
            writer.Write(userSeq);
            writer.Write(unitSeq);
            writer.Write((int)skillId);
            writer.Write((byte)(isOn == false ? 0 : 1));
            _client.SendPacket(writer, null);
        }

        // 이모티콘 보내기.
        public static void SendEmoticon(int position, int emoticonId, string str = null)
        {
            Debug.Log(Utility.Format("SendEmoticon position:{0}, emoticonId:{1}", position, emoticonId));

            var writer = _client.PreparePacket((ushort)PACKET_TYPE.P_EMOTICON);
            writer.Write(User.instance.seq);
            //writer.Write((byte)position);
            writer.Write(emoticonId);
            writer.WriteUnicode(str);
            _client.SendPacket(writer, null);
        }

        // 불일치 처리에 대한 검증용 오리지널 데이터 보내기.        
        public static void SendVerificationData(long userSeq, byte[] data)
        {
            Debug.Log("SendVerificationData userSeq :" + userSeq);

            var writer = _client.PreparePacket((ushort)PACKET_TYPE.P_DATA_VERIFY);
            writer.Write(userSeq);
            writer.Write((ushort)data.Length);
            writer.Write(data);
            _client.SendPacket(writer, null);
        }
        #endregion 전투 공용 단방향 send.

        #region 전투 공용 notify.
        // turn 시작.
        private static void OnNotifyReadyAction(PacketReader reader)
        {
            Debug.Log("OnNotifyReadyAction");

            var info = new PNotifyReadyAction(reader);

            _listenerBattleCommon?.OnReadyAction(info);
        }

        // 타일 선택.
        private static void OnNotifySelectTile(PacketReader reader)
        {
            Debug.Log("OnNotifySelectTile");

            var info = new PNotifySelectTile(reader);

            _listenerBattleCommon?.OnSelectTile(info);
        }

        // 액티브 스킬 준비 상태.
        private static void OnNotifyReadySkill(PacketReader reader)
        {
            Debug.Log("OnNotifyReadySkill");

            var info = new PNotifyActiveReady(reader);

            _listenerBattleCommon?.OnReadySkill(info);
        }

        // 이모티콘 사용.
        private static void OnNotifyEmoticon(PacketReader reader)
        {
            Debug.Log("OnNotifyUseEmoticon");

            var info = new PNotifyEmoticon(reader);

            _listenerBattleCommon?.OnEmoticon(info);
        }

        // 액션 실행.
        private static void OnNotifyRunAction(PacketReader reader)
        {
            Debug.Log("OnNotifyRunAction");

            var info = new PNotifyRunAction(reader);

            _listenerBattleCommon?.OnRunAction(info);
        }

        // 액션 timeout. 강제 턴 실행.
        private static void OnNotifyTimeoutAction(PacketReader reader)
        {
            Debug.Log("OnNotifyTimeoutAction");

            _listenerBattleCommon?.OnTimeoutAction();
        }

        // 턴 결과 체크 완료 알림.
        private static void OnNotifyEndTurn(PacketReader reader)
        {
            Debug.Log("OnNotifyForcedEndTurn");

            _listenerBattleCommon?.OnEndTurn();
        }

        // 특정 유저 disconnect.
        private static void OnNotifyDisconnectUser(PacketReader reader)
        {
            Debug.Log("OnNotifyDisconnectUser");

            var info = new PNotifyUserNetworkStatus(reader, false);

            _listenerBattleCommon?.OnDisconnectUser(info);
        }

        // 특정 유저 reconnect.
        private static void OnNotifyReconnectUser(PacketReader reader)
        {
            Debug.Log("OnNotifyReconnectUser");

            var info = new PNotifyUserNetworkStatus(reader, true);

            _listenerBattleCommon?.OnReconnectUser(info);
        }

        // 데이터 불일치 체크를 위한 검증용 데이터 수신 알림.
        private static void OnNotifyVerifyData(PacketReader reader)
        {
            Debug.Log("OnNotifyVerifyData");

            var info = new PNotifyDataVerify(reader);

            _listenerBattleCommon?.OnVerifyData(info);
        }
        #endregion //전투 공용 notify.
    }


    //================================================================================
    // battle 공용 request, notify packet 생성 클래스.
    //================================================================================
    #region battle common response, notify packet 정의.
    public class PTurnStart : PDefaultResponse
    {
        public long userSeqNow;
        public int turnCountNow;

        public PTurnStart(PacketReader reader) : base(reader)
        {
            if (!isSuccess)
            {
                return;
            }

            userSeqNow = reader.ReadLong();
            turnCountNow = reader.ReadUInt16();
        }
    }

    public class PTimeoutAction : PDefaultResponse
    {
        public long noAnswerUserSeq1;
        public long noAnswerUserSeq2;

        public PTimeoutAction(PacketReader reader) : base(reader)
        {
            if (!isSuccess)
            {
                return;
            }

            noAnswerUserSeq1 = reader.ReadLong();
            noAnswerUserSeq2 = reader.ReadLong();
        }
    }

    public class PNotifyReadyAction
    {          
        public DateTime startTime;

        public PNotifyReadyAction(PacketReader reader)
        {
            var serverTime = reader.ReadDouble();            
            startTime = ServerTime.ServerTimeStampToUtcTime(serverTime);
        }
    }   

    public class PNotifySelectTile
    {
        public long userSeqOwner;
        public int useMercenarySeq;
        public List<Tile> selectTileList;

        public PNotifySelectTile(PacketReader reader)
        {
            userSeqOwner = reader.ReadLong();
            useMercenarySeq = reader.ReadInt32();
            var tileList = reader.ReadString();
            var jsonData = JsonMapper.ToObject(tileList);
            selectTileList = new List<Tile>();
            int index = 0;
            for (int i = 0, count = jsonData.Count; i < count; i++)
            {
                index = int.Parse(jsonData[i].ToString());
                selectTileList.Add(BattleStage.instance.director.map.totalTiles[index]);
            }
        }
    }

    public class PNotifyActiveReady
    {
        public long userSeqOwner;
        public int useMercenarySeq;
        public long useSkillId;
        public bool isEnable;

        public PNotifyActiveReady(PacketReader reader)
        {
            userSeqOwner = reader.ReadLong();
            useMercenarySeq = reader.ReadInt32();
            useSkillId = reader.ReadInt32();
            isEnable = reader.ReadByte() == 0 ? false : true;
        }
    }

    public class PNotifyEmoticon
    {
        public long seq;
        public int position;
        public int emoticonId;
        public string msgStr;

        public PNotifyEmoticon(PacketReader reader)
        {
            //position = reader.ReadByte();
            seq = reader.ReadLong();
            emoticonId = reader.ReadInt32();
            msgStr = reader.ReadString();
        }

        public PNotifyEmoticon(int index, int emotionId, string msg = null)
        {
            position = index;
            emoticonId = emotionId;
            msgStr = msg;
        }
    }

    public class PNotifyRunAction
    {
        public long userSeqOwner;
        public int useMercenarySeq;
        public long useSkillId;
        public List<Tile> actionTileList;

        public PNotifyRunAction(PacketReader reader)
        {
            userSeqOwner = reader.ReadLong();
            useMercenarySeq = reader.ReadInt32();
            useSkillId = reader.ReadInt32();
            var tileList = reader.ReadString();
            var jsonData = JsonMapper.ToObject(tileList);
            actionTileList = new List<Tile>();
            int index = 0;
            for (int i = 0, count = jsonData.Count; i < count; i++)
            {
                index = int.Parse(jsonData[i].ToString());
                actionTileList.Add(BattleStage.instance.director.map.totalTiles[index]);
            }
        }
    }

    public class PNotifyUserNetworkStatus
    {
        public long userSeqTarget;
        public bool isConnect;

        public PNotifyUserNetworkStatus(PacketReader reader, bool isConnect)
        {
            userSeqTarget = reader.ReadLong();
            this.isConnect = isConnect;
        }
    }

    public class PNotifyDataVerify
    {
        public long senderSeq;
        public byte[] verifyData;

        public PNotifyDataVerify(PacketReader reader)
        {
            senderSeq   = reader.ReadLong();
            int dataLength = reader.ReadInt16();
            verifyData = reader.ReadBytes(dataLength);
        }
    }

    public class PRaidRoomCount : PDefaultResponse
    {
        public List<int> roomCountList;

        public PRaidRoomCount(PacketReader reader) : base(reader)
        {
            if (!isSuccess) return;

            roomCountList = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                roomCountList.Add(reader.ReadInt32());
            }
        }
    }
    #endregion battle common response, notify packet 정의.    
}