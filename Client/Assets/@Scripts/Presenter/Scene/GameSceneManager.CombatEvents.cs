using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    // 서버 전투/채팅 이벤트 수신: 피해, 회피, HP/부활/위치 보정, 경험치, 레벨업 연출.
    public partial class GameSceneManager
    {
        #region Method
        /// <summary>
        /// UI_GameSceneView(ChatContainer)에서 Enter로 전송한 메시지를 GameServer로 보낸다.
        /// 내 화면에는 여기서 직접 추가하지 않고, 서버가 되돌려주는 Game_ChatBroadcast(HandleChatReceived)를
        /// 통해 다른 접속자와 동일한 경로로 표시한다 - 메시지 순서/타임스탬프를 서버 기준으로 통일하기 위해서다.
        /// </summary>
        private void HandleChatMessageSubmitted(string message)
        {
            GameServerConnectManager.Instance?.SendChat(message);
        }

        /// <summary>
        /// GameServer로부터 받은 채팅(Game_ChatBroadcast, 내 메시지 포함)을 ChatContainer에 표시한다.
        /// </summary>
        private void HandleChatReceived(GameChatBroadcastPacket packet)
        {
            chatView?.AddChatMessage(packet.Nickname, packet.Message);
        }

        /// <summary>
        /// Game_DamageBroadcast는 전원(공격자 포함)에게 오지만, 이 메서드는 내(로컬 플레이어)가 맞은
        /// 경우만 처리한다. 다른 플레이어가 맞은 경우는 RemotePlayerManager가 별도로 구독해 처리한다.
        /// 체력은 서버가 계산한 RemainingHp를 그대로 반영한다(0이면 사망).
        /// </summary>
        private void HandleDamageReceived(GameDamageBroadcastPacket packet)
        {
            if (!IsLocalPlayer(packet.TargetId))
            {
                return;
            }

            ApplyLocalServerHp(packet.RemainingHp, spawnedPlayerModel.MaxHp, true);
            ShakeCameraOnHit();
        }

        /// <summary>
        /// Game_MonsterAttackBroadcast는 전원에게 오지만(공격 애니메이션은 RemoteMonsterManager가 전원 재생),
        /// 체력 반영은 내(로컬 플레이어)가 대상인 경우만 처리한다 - HandleDamageReceived(PvP)와 동일한 패턴.
        /// </summary>
        private void HandleMonsterAttackReceived(GameMonsterAttackBroadcastPacket packet)
        {
            if (!IsLocalPlayer(packet.TargetPlayerId))
            {
                return;
            }

            ApplyLocalServerHp(packet.RemainingHp, spawnedPlayerModel.MaxHp, true);
            ShakeCameraOnHit();
        }

        // 내가 맞았을 때 카메라를 짧고 약하게 흔든다(피격 느낌). 내 공격이 명중한 경우에는 흔들지 않는다 - 계속 흔들리면 어지럽다.
        private const float HitShakeAmplitude = 0.12f;
        private const float HitShakeDuration = 0.2f;

        private static void ShakeCameraOnHit()
        {
            CameraOrbitController.Instance?.Shake(HitShakeAmplitude, HitShakeDuration);
        }

        /// <summary>
        /// 몬스터 공격이 대쉬 회피에 막혔을 때(Game_MonsterAttackDodgedBroadcast) 내(로컬 플레이어)가 대상인 경우만 처리해
        /// EffectBone 위치에 회피 이펙트를 재생한다. HP는 변하지 않는다. 다른 플레이어의 회피는 RemotePlayerManager가 처리한다.
        /// </summary>
        private void HandleMonsterAttackDodged(GameMonsterAttackDodgedBroadcastPacket packet)
        {
            if (!IsLocalPlayer(packet.TargetPlayerId) || localPlayerInstance == null)
            {
                return;
            }

            Transform effectAnchor = localPlayerInstance.transform.Find("EffectBone");
            CharacterVfxManager.Instance?.PlayDodgeEffect(effectAnchor);
        }

        /// <summary>
        /// 피격이 아닌 이유(레벨업 등)로 서버가 내 체력을 바꿨을 때(Game_PlayerHpBroadcast) 반영한다.
        /// 다른 플레이어의 체력 변화는 RemotePlayerManager가 처리한다.
        /// </summary>
        private void HandlePlayerHpChanged(GamePlayerHpBroadcastPacket packet)
        {
            if (!IsLocalPlayer(packet.PlayerId))
            {
                return;
            }

            ApplyLocalServerHp(packet.CurrentHp, packet.MaxHp, false);
        }

        /// <summary>
        /// 서버가 알려 준 내 마나(Game_PlayerMpUpdate)를 반영한다. 자연 회복, 레벨업/부활로 가득 참, 스킬 사용 소모가 모두 이 경로로 온다.
        /// 서버가 본인에게만 보내는 알림이지만 방어적으로 내 캐릭터인지 확인한다.
        /// </summary>
        private void HandlePlayerMpChanged(GamePlayerMpUpdatePacket packet)
        {
            if (!IsLocalPlayer(packet.PlayerId) || spawnedPlayerModel == null)
            {
                return;
            }

            spawnedPlayerModel.ApplyServerMp(packet.CurrentMp, packet.MaxMp);
        }

        /// <summary>
        /// 서버가 나를 자동 부활시켰을 때(Game_PlayerRevived) 서버가 정한 부활 위치로 옮긴 뒤 체력을 반영하고
        /// 조작을 다시 켠다. 서버도 이미 그 위치를 내 위치로 알고 있으므로 이후 이동 검증의 기준점과 일치한다.
        /// </summary>
        private void HandlePlayerRevived(GamePlayerRevivedPacket packet)
        {
            if (!IsLocalPlayer(packet.PlayerId))
            {
                return;
            }

            WarpLocalPlayer(new Vector3(packet.X, packet.Y, packet.Z), Quaternion.Euler(0f, packet.RotationY, 0f));
            ApplyLocalServerHp(packet.CurrentHp, packet.MaxHp, false);
        }

        /// <summary>
        /// 서버가 내 이동을 거부했을 때(Game_PositionCorrection, 허용 속도 초과) 서버가 마지막으로 인정한 위치로 되돌린다.
        /// </summary>
        private void HandlePositionCorrected(GamePositionCorrectionPacket packet)
        {
            WarpLocalPlayer(new Vector3(packet.X, packet.Y, packet.Z), Quaternion.Euler(0f, packet.RotationY, 0f));
        }

        private bool IsLocalPlayer(long playerId)
        {
            return spawnedPlayerModel != null && SaveDataManager.Instance != null
                && playerId == SaveDataManager.Instance.SelectedCharacterId;
        }

        /// <summary>
        /// 서버가 보낸 내 체력을 반영하고, 그 결과 사망/부활했다면 이동·공격 조작을 끄거나 다시 켠다.
        /// 사망 중 조작을 막는 건 연출용이다 - 사망한 플레이어의 공격/맵 이동은 서버도 거부한다.
        /// </summary>
        private void ApplyLocalServerHp(int currentHp, int maxHp, bool wasHit)
        {
            bool wasDead = spawnedPlayerModel.IsDead;
            spawnedPlayerModel.ApplyServerHp(currentHp, maxHp, wasHit);

            // 피격이 아닌 체력 변화(레벨업으로 최대 체력이 오름, 부활)는 인벤토리 스탯 패널의 최대 체력 표시도 바꾼다.
            if (!wasHit)
            {
                RefreshStatsPanel();
            }

            if (wasDead == spawnedPlayerModel.IsDead)
            {
                return;
            }

            SetLocalPlayerControlEnabled(!spawnedPlayerModel.IsDead);

            // 사망하면 부활까지 남은 시간을 팝업으로 보여주고, 부활하면(서버 부활 알림, 또는 재접속으로 살아 있는 상태로 입장) 닫는다.
            if (spawnedPlayerModel.IsDead)
            {
                chatView?.AddChatMessage("시스템", "사망했습니다. 잠시 후 부활합니다.");
                respawnPopupView?.Show(ReviveDelaySeconds);
            }
            else
            {
                respawnPopupView?.Hide();
            }
        }

        private void SetLocalPlayerControlEnabled(bool isEnabled)
        {
            if (localPlayerInstance == null)
            {
                return;
            }

            if (localPlayerInstance.TryGetComponent(out PlayerMoveController moveController))
            {
                moveController.enabled = isEnabled;
            }

            if (localPlayerAttackController != null)
            {
                localPlayerAttackController.enabled = isEnabled;
            }

            // 이동 중에 쓰러지면 남은 속도로 미끄러지지 않게 멈춘다.
            if (!isEnabled && localPlayerInstance.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 로컬 플레이어를 지정한 위치/회전으로 즉시 옮긴다(맵 전환 진입, 서버 부활 위치, 서버 위치 보정 공용).
        /// Rigidbody가 있으면 남은 속도를 없애고 물리 위치로 옮겨, 다음 물리 스텝에서 원래 자리로 끌려가지 않게 한다.
        /// </summary>
        private void WarpLocalPlayer(Vector3 position, Quaternion rotation)
        {
            if (localPlayerInstance == null)
            {
                return;
            }

            if (localPlayerInstance.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = position;
                rb.rotation = rotation;
            }
            else
            {
                localPlayerInstance.transform.SetPositionAndRotation(position, rotation);
            }
        }

        /// <summary>
        /// PlayerAttackController.MonsterTargeted(내가 몬스터를 공격할 때마다)를 그대로 UI_MonsterTargetView에
        /// 전달해 몬스터 이름/등급/체력바를 갱신하고, 타겟-로스트 타임아웃 판정에 쓸 시각을 갱신한다.
        /// </summary>
        private void HandleMonsterTargeted(RemoteMonsterController targetMonster)
        {
            monsterTargetView?.BindTarget(targetMonster);

            if (targetMonster != null)
            {
                lastMonsterTargetedTime = Time.time;
            }
        }

        /// <summary>
        /// 내가 몬스터를 처치해 GameServer가 계산한 경험치/레벨(Game_ExpGainBroadcast, 처치자 본인에게만 옴)을 화면에 반영한다.
        /// DB 저장은 GameServer가 MainServer에 직접 하므로(서버 간 API) 클라이언트는 저장을 요청하지 않는다.
        /// </summary>
        private void HandleExpGained(GameExpGainBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null)
            {
                return;
            }

            spawnedPlayerModel.ApplyExpGain(packet.TotalExp, packet.Level, packet.ExpToNextLevel);

            if (packet.DidLevelUp)
            {
                PlayLevelUpEffect();

                // 레벨이 오르면 능력치(str/agi/intel)와 공격력/방어력이 바로 달라진다(PlayerCharacterModel.ApplyLevel). 최대 체력은
                // 곧이어 오는 Game_PlayerHpBroadcast에서 바뀌므로(ApplyLocalServerHp) 그때 한 번 더 갱신된다.
                RefreshStatsPanel();
            }
        }

        /// <summary>
        /// 레벨업 시 로컬 플레이어의 EffectBone(발 밑 앵커) 위치에 이펙트를 재생한다.
        /// CharacterVfxManager가 ObjectPoolManager를 통해 대여/자동 반환을 처리하므로 여기서는 앵커만 넘겨주면 된다.
        /// </summary>
        private void PlayLevelUpEffect()
        {
            if (localPlayerInstance == null)
            {
                return;
            }

            Transform effectAnchor = localPlayerInstance.transform.Find("EffectBone");
            CharacterVfxManager.Instance?.PlayLevelUpEffect(effectAnchor);
        }


        /// <summary>
        /// 회복포션 사용이 서버에서 성공으로 확인될 때마다(HandleUseItemResult) 로컬 플레이어의
        /// EffectBone 위치에 이펙트를 재생한다.
        /// </summary>
        private void PlayHpPotionEffect()
        {
            if (localPlayerInstance == null)
            {
                return;
            }

            Transform effectAnchor = localPlayerInstance.transform.Find("EffectBone");
            CharacterVfxManager.Instance?.PlayHpPotionEffect(effectAnchor);
        }

        #endregion
    }
}
