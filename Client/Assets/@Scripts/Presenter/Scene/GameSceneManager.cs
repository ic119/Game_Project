using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Utils;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    public class GameSceneManager : MonoBehaviour
    {
        #region Variable
        private const string gameSceneTag = "GameScene";
        #endregion

        #region LifeCycle
        private void Start()
        {
            LoadAndInstantiateGameSceneAssets();
        }
        #endregion

        #region Method
        /// <summary>
        /// AddressableAssetModelSO에서 tags가 "GameScene"인 항목의 preloadAddressableKeys(예: UI_GameScene)를 로드하여
        /// 이 GameSceneManager(this.transform)의 자식으로 직접 생성한다.
        /// GameScene 전용 에셋은 씬이 언로드될 때 함께 파괴되어야 하므로, ObjectPoolManager(PersistAcrossScenes) 기반인
        /// GameManager.LoadAndInstantiateByTag와는 별도로 여기서 직접 로드/인스턴스화한다.
        /// </summary>
        private async void LoadAndInstantiateGameSceneAssets()
        {
            if (GameManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("GameManager.Instance가 null입니다.");
                return;
            }

            List<AddressableAssetKey> keys = await GameManager.Instance.LoadAddressableKeysByTagAsync(gameSceneTag);

            if (keys == null || this == null)
            {
                return;
            }

            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            foreach (AddressableAssetKey key in keys)
            {
                if (key == AddressableAssetKey.None)
                {
                    continue;
                }

                string keyString = key.ToString();

                AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(keyString, prefab =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    if (prefab == null)
                    {
                        DebugLogManager.GenerateErrorMessage<GameSceneManager>($"GameScene Addressable 로드 실패 Key : {keyString}");
                        return;
                    }

                    GameObject instance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, transform);

                    // Farm은 단순 프리로드 대상이 아니라 플레이어가 실제로 배치될 맵이므로,
                    // 생성 직후 맵 안의 RespawnPoint를 찾아 그 자리에 선택된 캐릭터를 스폰한다.
                    if (key == AddressableAssetKey.Farm)
                    {
                        SpawnPlayerAtRespawnPoint(instance);
                    }
                });
            }
        }

        /// <summary>
        /// Farm 맵 인스턴스 하위에서 "RespawnPoint" 이름의 Transform을 찾아 그 위치에 플레이어 캐릭터를 스폰한다.
        /// </summary>
        private void SpawnPlayerAtRespawnPoint(GameObject _farmInstance)
        {
            if (_farmInstance == null)
            {
                return;
            }

            Transform respawnPoint = FindChildRecursive(_farmInstance.transform, "RespawnPoint");

            if (respawnPoint == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("Farm 맵에서 RespawnPoint를 찾을 수 없습니다.");
                return;
            }

            SpawnPlayerCharacter(respawnPoint);
        }

        private static Transform FindChildRecursive(Transform _root, string _name)
        {
            if (_root.name == _name)
            {
                return _root;
            }

            foreach (Transform child in _root)
            {
                Transform found = FindChildRecursive(child, _name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// BasicCharacter를 Addressable로 로드해 _respawnPoint의 자식으로 생성하고,
        /// SaveDataManager에 저장된 선택 캐릭터의 외형(헤어/눈/입)을 적용한다.
        /// </summary>
        private void SpawnPlayerCharacter(Transform _respawnPoint)
        {
            if (AddressableAssetManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("AddressableAssetManager.Instance가 null입니다.");
                return;
            }

            AddressableAssetManager.Instance.LoadPrefabAddress<GameObject>(AddressableAssetKey.BasicCharacter.ToString(), prefab =>
            {
                if (this == null)
                {
                    return;
                }

                if (prefab == null)
                {
                    DebugLogManager.GenerateErrorMessage<GameSceneManager>($"플레이어 캐릭터 로드 실패 Key : {AddressableAssetKey.BasicCharacter}");
                    return;
                }

                GameObject playerInstance = AddressableAssetManager.Instance.InstantiatePrefab(prefab, _respawnPoint);
                playerInstance.transform.localPosition = Vector3.zero;
                playerInstance.transform.localRotation = Quaternion.identity;

                ApplySelectedCharacterCustomization(playerInstance);
                AssignPlayerToFollowCamera(playerInstance.transform);

                // RequireComponent로 Rigidbody/CapsuleCollider가 함께 추가되어, 스폰 직후 중력을 받아 지면에 착지하고
                // 화살표 키로 이동/회전할 수 있게 된다.
                playerInstance.AddComponent<PlayerMoveController>();
            });
        }

        /// <summary>
        /// 씬의 CinemachineCamera(CM_PlayerFollowCamera)가 방금 스폰된 플레이어를 추적하도록 Follow 타깃을 연결한다.
        /// Farm/RespawnPoint 등 씬 구성 에셋과 달리 카메라는 GameScene.unity에 이미 배치되어 있으므로 여기서는 찾아서 연결만 한다.
        /// </summary>
        private void AssignPlayerToFollowCamera(Transform _playerTransform)
        {
            CinemachineCamera followCamera = FindAnyObjectByType<CinemachineCamera>();

            if (followCamera == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("씬에서 CinemachineCamera를 찾을 수 없습니다.");
                return;
            }

            // 이동/전투 카메라는 ThirdPersonFollow가 Follow 대상의 회전을 그대로 카메라 방향으로 쓰므로 LookAt은 필요 없다.
            followCamera.Follow = _playerTransform;
        }

        /// <summary>
        /// SaveDataManager.SelectedCharacterId를 서버에서 다시 조회하여(헤어/눈/입 포함),
        /// 방금 생성한 플레이어 인스턴스의 CharacterCustomModel에 적용한다.
        /// </summary>
        private void ApplySelectedCharacterCustomization(GameObject _playerInstance)
        {
            if (SaveDataManager.Instance == null || !SaveDataManager.Instance.SelectedCharacterId.HasValue)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("선택된 캐릭터가 없어 외형을 적용할 수 없습니다.");
                return;
            }

            if (!_playerInstance.TryGetComponent(out CharacterCustomModel customModel))
            {
                return;
            }

            SaveDataManager.Instance.FetchCharacterDetailAsync(SaveDataManager.Instance.SelectedCharacterId.Value, userSaveData =>
            {
                if (_playerInstance == null || userSaveData == null)
                {
                    return;
                }

                customModel.ApplyCustomization(userSaveData.hairIndex, userSaveData.eyeIndex, userSaveData.mouthIndex);
            });
        }
        #endregion
    }
}
