using Incheol.Modules.Networking;
using Incheol.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Incheol.Modules
{
    // 서버 알림을 게임 쪽에 전달하는 이벤트 선언(메인 스레드에서 호출된다).
    public partial class GameServerConnectManager
    {
        /// <summary>
        /// 맵 이동 요청(Game_MapChangeRequest)을 서버가 승인했을 때(Game_MapChangeAck). 서버가 정한 내 위치/체력(Self)과 새 맵 시야 안의
        /// 플레이어/몬스터 목록을 담는다. 목록은 이 이벤트를 받은 쪽이 맵을 교체한 뒤 DispatchMapChangeEntities로 적용한다 -
        /// 맵을 교체하기 전에 스폰하면 교체하면서 지워지기 때문이다.
        /// </summary>
        public event Action<GameEnterAckPacket> OnMapChangeAcked;

        /// <summary>
        /// 맵 이동 요청을 서버가 거부했을 때(Game_MapChangeRejected). 서버 상태는 그대로이므로 이전 맵을 유지하면 된다.
        /// </summary>
        public event Action<GameMapChangeRejectedPacket> OnMapChangeRejected;

        public event Action<GamePlayerInfo> OnPlayerJoined;
        public event Action<long> OnPlayerLeft;
        /// <summary>
        /// 서버 방 틱(20Hz)마다 위치가 바뀐 원격 플레이어/몬스터 목록이 묶여 온다(Game_WorldSnapshot).
        /// </summary>
        public event Action<GameWorldSnapshotPacket> OnWorldSnapshot;

        /// <summary>
        /// 몬스터가 내 관심 영역(시야) 밖으로 나갔을 때 발생한다(Game_MonsterLeaveView). 시야에 들어올 때는 OnMonsterSpawned,
        /// 원격 플레이어의 시야 진입/이탈은 기존 OnPlayerJoined/OnPlayerLeft로 온다.
        /// </summary>
        public event Action<long> OnMonsterLeftView;
        public event Action<GameChatBroadcastPacket> OnChatReceived;
        public event Action<GameDamageBroadcastPacket> OnDamageReceived;

        /// <summary>
        /// 다른 플레이어가 공격 모션을 취했을 때 발생한다(Game_AttackAnimationBroadcast). 본인의 공격은
        /// 로컬에서 즉시 재생하므로 이 이벤트로 오지 않는다 - 원격 캐릭터 전용이다.
        /// </summary>
        public event Action<GameAttackAnimationBroadcastPacket> OnAttackAnimationReceived;

        /// <summary>
        /// 다른 플레이어가 대쉬를 시작했을 때 발생한다(Game_DashBroadcast). 본인의 대쉬는 로컬에서 즉시 재생하므로
        /// 이 이벤트로 오지 않는다 - 원격 캐릭터 전용이다.
        /// </summary>
        public event Action<GameDashBroadcastPacket> OnDashReceived;

        /// <summary>
        /// 보물상자가 열렸을 때(Game_ChestOpenBroadcast) 발생한다. 본인이 방금 연 경우/다른 플레이어가 연 경우/
        /// 방에 새로 입장해 이미 열린 상자를 따라잡는 경우를 구분하지 않고 전부 이 이벤트로 온다 - 해당 ChestId를
        /// 가진 TreasureChestInteractionController가 알아서 자기 것인지 판단한다.
        /// </summary>
        public event Action<GameChestOpenBroadcastPacket> OnChestOpened;

        /// <summary>
        /// 방에 입장/맵 이동한 직후 서버가 알려주는 "지금 서 있는 상자 목록"(Game_ActiveChestsNotify). 고정 상자와
        /// 후보에서 뽑힌 상자가 함께 온다. 이미 열린 상자를 알리는 OnChestOpened보다 먼저 발생한다.
        /// </summary>
        public event Action<GameActiveChestsPacket> OnActiveChestsReceived;

        /// <summary>
        /// 방에 입장/맵 이동한 직후 서버가 알려주는 "던전 게이트가 선 후보 지점"(Game_ActiveGateNotify). 상자 목록 다음에 온다.
        /// 게이트가 없는 맵이면 HasGate가 false로 온다.
        /// </summary>
        public event Action<GameActiveGatePacket> OnActiveGateReceived;

        /// <summary>
        /// 리스폰으로 새 상자가 생겼을 때(Game_ChestSpawnBroadcast). 방에 있는 모두에게 온다.
        /// </summary>
        public event Action<GameChestInfo> OnChestSpawned;

        /// <summary>
        /// 열린 상자가 잔존 시간이 지나 사라졌을 때(Game_ChestDespawnBroadcast). 인자는 사라진 상자의 chestId다.
        /// </summary>
        public event Action<string> OnChestDespawned;

        /// <summary>
        /// 다른 플레이어(또는 본인)의 장착 장비가 바뀌었을 때(Game_EquipmentChangedBroadcast). 원격 캐릭터의 외형을 갱신하는 데 쓴다.
        /// </summary>
        public event Action<GameEquipmentChangedPacket> OnEquipmentChanged;
        public event Action<GameMonsterInfo> OnMonsterSpawned;
        public event Action<GameMonsterDamageBroadcastPacket> OnMonsterDamaged;
        public event Action<GameMonsterDieBroadcastPacket> OnMonsterDied;
        public event Action<GameMonsterAttackBroadcastPacket> OnMonsterAttacked;

        /// <summary>
        /// 몬스터가 공격을 시작했을 때(Game_MonsterAttackStartBroadcast, 선딜 시작). 공격 모션 재생에 쓴다 -
        /// 피해는 선딜이 끝난 뒤 OnMonsterAttacked(명중) 또는 OnMonsterAttackDodged(회피)로 따로 온다.
        /// </summary>
        public event Action<GameMonsterAttackStartBroadcastPacket> OnMonsterAttackStarted;

        /// <summary>
        /// 몬스터 공격이 대쉬 회피에 막혔을 때(Game_MonsterAttackDodgedBroadcast). HP 변화는 없고 회피 이펙트 재생에 쓴다.
        /// </summary>
        public event Action<GameMonsterAttackDodgedBroadcastPacket> OnMonsterAttackDodged;

        /// <summary>
        /// 보스가 스킬을 준비하기 시작했을 때(Game_BossSkillTelegraphBroadcast, 예고). 바닥에 위험 범위를 DurationMs 동안 차오르게
        /// 보여주고 시전 모션을 재생한다. 판정은 예고가 끝나는 순간 서버가 하며 결과는 OnMonsterAttacked/OnMonsterAttackDodged로 온다.
        /// </summary>
        public event Action<GameBossSkillTelegraphBroadcastPacket> OnBossSkillTelegraph;

        /// <summary>
        /// 보스 스킬의 예고가 끝났을 때(Game_BossSkillEndBroadcast). Executed가 true면 발동(위험 범위 표시를 걷고 타격 연출),
        /// false면 취소(보스 사망 등 - 표시만 걷는다).
        /// </summary>
        public event Action<GameBossSkillEndBroadcastPacket> OnBossSkillEnd;
        public event Action<GameExpGainBroadcastPacket> OnExpGained;
        public event Action<GameLootBroadcastPacket> OnLootReceived;
        public event Action<GamePlayerHpBroadcastPacket> OnPlayerHpChanged;

        /// <summary>
        /// 내 마나가 바뀌었을 때(Game_PlayerMpUpdate). 자연 회복, 레벨업/부활로 가득 참, 스킬 사용 소모가 모두 이 이벤트로 온다.
        /// 서버가 본인에게만 보내므로 로컬 플레이어의 값이다.
        /// </summary>
        public event Action<GamePlayerMpUpdatePacket> OnPlayerMpChanged;
        public event Action<GamePlayerRevivedPacket> OnPlayerRevived;
        public event Action<GameUseItemResultPacket> OnUseItemResult;
        public event Action<GamePositionCorrectionPacket> OnPositionCorrected;

        /// <summary>
        /// 입장(최초/재접속)이 받아들여졌을 때(Game_EnterAck) 서버가 정한 본인 상태와 함께 발생한다. 같은 응답의
        /// 원격 플레이어/몬스터(OnPlayerJoined/OnMonsterSpawned)보다 먼저 온다.
        /// </summary>
        public event Action<GamePlayerInfo> OnEntered;

        /// <summary>
        /// 예기치 않게 끊겨 자동 재접속을 시도할 때마다 (시도 번호, 최대 횟수)와 함께 발생한다.
        /// </summary>
        public event Action<int, int> OnReconnecting;

        /// <summary>
        /// 자동 재접속에 성공해 다시 입장했을 때 발생한다(같은 응답의 OnEntered/OnPlayerJoined/OnMonsterSpawned 뒤).
        /// </summary>
        public event Action OnReconnected;

        /// <summary>
        /// 연결이 예기치 않게 끊겼고 자동 재접속도 모두 실패했을 때 발생한다.
        /// </summary>
        public event Action OnDisconnected;
        public event Action<string> OnServerError;

        /// <summary>
        /// 서버가 이 연결을 강제로 끊었을 때(같은 캐릭터로 다른 곳에서 접속 등) 사유 문자열과 함께 발생한다.
        /// 이 경우 OnDisconnected는 발생하지 않는다.
        /// </summary>
        public event Action<string> OnKicked;
    }
}
