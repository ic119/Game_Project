using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using System;
using Unity.Cinemachine;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    /// <summary>
    /// 로컬 플레이어 캐릭터를 생성하고 조작에 필요한 컨트롤러를 붙인다. 외형/스탯 적용과 서버 입장은 호출한 쪽(GameSceneManager)이
    /// 콜백을 받아 이어서 처리한다.
    /// </summary>
    public class LocalPlayerSpawner
    {
        private readonly MonoBehaviour owner;

        /// <param name="owner">GameSceneManager. 플레이어가 그 transform 밑에 생성되고, 비동기 로드가 끝나기 전에 파괴됐는지도 확인한다.</param>
        public LocalPlayerSpawner(MonoBehaviour owner)
        {
            this.owner = owner;
        }

        /// <summary>
        /// BasicCharacter를 Addressable로 로드해 owner.transform 밑에 생성하고, respawnPoint의 위치/회전값만 가져다 배치한다(그 자식으로
        /// 만들지는 않는다 - 맵 프리팹 하위에 있는 RespawnPoint에 종속되면 맵이 파괴/교체될 때 플레이어도 함께 파괴될 위험이 있다).
        /// 생성이 끝나면 onSpawned(플레이어, 공격 컨트롤러)를 호출한다. owner가 그 전에 파괴됐으면 아무것도 하지 않는다.
        /// </summary>
        public void Spawn(Transform respawnPoint, Action<GameObject, PlayerAttackController> onSpawned)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<LocalPlayerSpawner>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.BasicCharacter.ToString(), prefab =>
            {
                if (owner == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<LocalPlayerSpawner>($"플레이어 캐릭터 로드 실패 Key : {AddressableAssetKey.BasicCharacter}");
                    return;
                }

                GameObject playerInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, owner.transform);
                playerInstance.transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);

                AssignToFollowCamera(playerInstance.transform);

                // RequireComponent로 Rigidbody/CapsuleCollider가 함께 추가되어, 스폰 직후 중력을 받아 지면에 착지하고
                // WASD로 카메라 기준 이동할 수 있게 된다(이동 방향으로 몸이 자동으로 돈다).
                playerInstance.AddComponent<PlayerMoveController>();

                // 일정 주기로 자신의 위치/회전을 GameServer(Game_MoveRequest)로 전송한다.
                playerInstance.AddComponent<PlayerNetworkSender>();

                // C 입력으로 전방의 원격 플레이어/몬스터를 공격한다.
                PlayerAttackController attackController = playerInstance.AddComponent<PlayerAttackController>();

                // 숫자 키 1~4로 액티브 스킬을 쓴다(PlayerAttackController/PlayerMoveController를 찾아 쓰므로 둘보다 뒤에 붙인다).
                playerInstance.AddComponent<PlayerSkillController>();

                onSpawned(playerInstance, attackController);
            });
        }

        /// <summary>
        /// 씬의 CinemachineCamera(CM_PlayerFollowCamera)가 방금 스폰된 플레이어를 추적하도록 Follow 타깃을 연결한다.
        /// 카메라는 GameScene.unity에 이미 배치되어 있으므로 여기서는 찾아서 연결만 한다.
        /// </summary>
        private static void AssignToFollowCamera(Transform playerTransform)
        {
            CinemachineCamera followCamera = UnityEngine.Object.FindAnyObjectByType<CinemachineCamera>();

            if (followCamera == null)
            {
                DebugLogManager.GenerateErrorMessage<LocalPlayerSpawner>("씬에서 CinemachineCamera를 찾을 수 없습니다.");
                return;
            }

            // 이동/전투 카메라는 수평 45도로 고정된 월드 기준 CinemachineFollow + RotationComposer라 캐릭터가 돌아도 화면이 돌지 않는다.
            // CustomLookAtTarget이 꺼져 있어 LookAt은 Follow 대상을 그대로 쓰므로 따로 지정하지 않는다.
            followCamera.Follow = playerTransform;
        }
    }
}
