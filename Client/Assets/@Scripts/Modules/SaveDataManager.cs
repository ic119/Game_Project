using Incheol.Utils;
using System;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 유저의 캐릭터 세이브 데이터를 서버(POST /api/characters, GET/PUT/DELETE /api/characters/me)와 동기화하고,
    /// PlayerPrefs를 오프라인/즉시 조회용 로컬 캐시로 사용하는 매니저.
    /// </summary>
    public class SaveDataManager : SingletonObject<SaveDataManager>
    {
        private const string SaveDataKey = "UserSaveData";
        private const string CharacterApiPath = "/api/characters";
        private const string CharacterMeApiPath = "/api/characters/me";

        protected override bool PersistAcrossScenes => true;

        private UserSaveData cachedSaveData;
        private const string CharacterCountKey = "UserCharacterCount";
        private int? cachedCharacterCount;

        /// <summary>
        /// 로컬에 캐시된, 이 계정이 보유한 캐릭터 총 개수(서버 응답이 있으면 그 값으로 동기화됨).
        /// </summary>
        public int CharacterCount => GetCharacterCount();

        private int GetCharacterCount()
        {
            if (!cachedCharacterCount.HasValue)
            {
                cachedCharacterCount = PlayerPrefs.GetInt(CharacterCountKey, 0);
            }
            return cachedCharacterCount.Value;
        }


        // 서버 DTO(MainServer.CharacterServer.DTOs)와 필드명을 맞춘 전송 전용 바디.
        // UserSaveData(클라이언트 내부 모델)와 서버 계약을 분리하기 위해 네트워크 경계에서만 사용한다.
        [Serializable] private class CreateCharacterRequestBody { public string _nickname; public int _hairIndex; public int _eyeIndex; public int _mouthIndex; }
        [Serializable] private class UpdateCustomizationRequestBody { public int _hairIndex; public int _eyeIndex; public int _mouthIndex; }
        [Serializable] private class CharacterResponseBody { public long _id; public string _nickname; public int _hairIndex; public int _eyeIndex; public int _mouthIndex; public int _str; public int _agi; public int _intel; public int _level; public string _lastLoginAt; public string _createdAt; }

        /// <summary>
        /// 로컬 캐시(메모리 → PlayerPrefs 순) 기준으로 세이브 데이터가 있는지 즉시(동기) 반환한다.
        /// 최신 서버 상태를 보장하려면 FetchFromServerAsync를 먼저 호출해 캐시를 갱신해야 한다.
        /// </summary>
        public bool HasSaveData => Load() != null;

        /// <summary>
        /// 로컬 캐시에서 즉시 조회한다. 네트워크 요청 없이 동기적으로 동작해야 하는
        /// UI(예: '이어하기' 버튼 표시 여부)에서 사용한다.
        /// </summary>
        public UserSaveData Load()
        {
            if (cachedSaveData != null)
            {
                return cachedSaveData;
            }

            if (!PlayerPrefs.HasKey(SaveDataKey))
            {
                return null;
            }

            string json = PlayerPrefs.GetString(SaveDataKey);
            cachedSaveData = JsonUtility.FromJson<UserSaveData>(json);
            return cachedSaveData;
        }

        /// <summary>
        /// 서버에서 캐릭터 세이브 데이터를 조회해 로컬 캐시(메모리 + PlayerPrefs)에 반영한다.
        /// 로그인 직후 한 번 호출해 최신 상태로 동기화하는 용도. 서버에 캐릭터가 아직 없는 신규 유저의 경우
        /// 실패로 보고되며, 이 경우 로컬 캐시는 손대지 않고 그대로 둔다.
        /// </summary>
        public void FetchFromServerAsync(Action<bool> _onComplete = null)
        {
            _ = FetchFromServerAsyncInternal(_onComplete);
        }

        /// <summary>
        /// 캐릭터 최초 생성 시 호출한다(POST /api/characters). UI_CharacterCreatePopup.OnCreateRequested(Func&lt;UserSaveData, bool&gt;)가
        /// 동기 반환을 요구하므로, 로컬 캐시에는 즉시 반영(낙관적 UI)하고 서버 저장은 백그라운드로 진행한다.
        /// 백그라운드 저장이 실패해도 사용자에 노출되지는 않으며(CreateAsyncInternal이 에러만 남김), 다음 로그인 시 FetchFromServerAsync로 맞춘다.
        /// </summary>
public bool CreateNew(UserSaveData _saveData)
        {
            if (_saveData == null)
            {
                return false;
            }

            // 이번 생성으로 이 계정이 보유하게 되는 캐릭터 총 개수를 계산해 요청 바디에 함께 실어 보낸다.
            _saveData.characterCount = GetCharacterCount() + 1;

            ApplyToLocalCache(_saveData);
            SaveAsync(_saveData);
            return true;
        }

        /// <summary>
        /// 이미 생성된 캐릭터의 외형(헤어/눈/입)을 서버에 저장(PUT /api/characters/me)하고, 성공했을 때만 로컬 캐시를 갱신한다.
        /// 실패 시 로컬 캐시는 이전 상태를 유지해 서버와 클라이언트 상태가 어긋나지 않게 한다.
        /// </summary>
        public void SaveAsync(UserSaveData _saveData, Action<bool> _onComplete = null)
        {
            _ = SaveAsyncInternal(_saveData, _onComplete);
        }

        /// <summary>
        /// 현재 캐시된 세이브 데이터를 기반으로 외형(헤어/눈/입)만 갱신해 서버에 저장한다.
        /// 캐시된 세이브 데이터가 없으면(캐릭터 생성 전) 실패로 처리한다 - 최초 생성은 UI_CharacterCreatePopup의
        /// OnCreateRequested 경로를 통해야 한다.
        /// </summary>
        public void UpdateCharacterCustomization(int _hairIndex, int _eyeIndex, int _mouthIndex, Action<bool> _onComplete = null)
        {
            UserSaveData current = Load();
            if (current == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("저장된 세이브 데이터가 없어 외형만 갱신할 수 없습니다.");
                _onComplete?.Invoke(false);
                return;
            }

            current.hairIndex = _hairIndex;
            current.eyeIndex = _eyeIndex;
            current.mouthIndex = _mouthIndex;

            SaveAsync(current, _onComplete);
        }

        /// <summary>
        /// 서버의 캐릭터 세이브 데이터를 삭제(DELETE)하고, 성공했을 때만 로컬 캐시도 비운다.
        /// 파괴적 작업이므로(생성과 달리) 낙관적으로 처리하지 않고 서버 확인 후에만 로컬 상태를 바꾼다.
        /// </summary>
        public void DeleteAsync(Action<bool> _onComplete = null)
        {
            _ = DeleteAsyncInternal(_onComplete);
        }

        private async Awaitable FetchFromServerAsyncInternal(Action<bool> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            (bool success, string body, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterMeApiPath, "GET");

            if (!success || string.IsNullOrEmpty(body))
            {
                // 신규 유저(캐릭터 생성 전)는 서버가 404 등을 내려줄 수 있다 - 정상 상황이므로 로컬 캐시를 그대로 둔다.
                _onComplete?.Invoke(false);
                return;
            }

            CharacterResponseBody response = JsonUtility.FromJson<CharacterResponseBody>(body);
            if (response == null || string.IsNullOrEmpty(response._nickname))
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"세이브 데이터 응답 파싱에 실패했습니다 : {body}");
                _onComplete?.Invoke(false);
                return;
            }

            UserSaveData serverData = UserSaveData.CreateDefault(response._nickname, response._hairIndex, response._eyeIndex, response._mouthIndex);
            ApplyToLocalCache(serverData);
            _onComplete?.Invoke(true);
        }

        /// <summary>
        /// 캐릭터를 최초 생성(POST /api/characters)한다. 이미 캐릭터가 있는 계정이면 서버가 409를 반환한다.
        /// </summary>
        private async Awaitable CreateAsyncInternal(UserSaveData _saveData, Action<bool> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            var requestBody = new CreateCharacterRequestBody
            {
                _nickname = _saveData.nickname,
                _hairIndex = _saveData.hairIndex,
                _eyeIndex = _saveData.eyeIndex,
                _mouthIndex = _saveData.mouthIndex
            };
            string json = JsonUtility.ToJson(requestBody);
            (bool success, string _, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterApiPath, "POST", json);

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 생성 저장 실패 : {error}");
                _onComplete?.Invoke(false);
                return;
            }

            _onComplete?.Invoke(true);
        }

        /// <summary>
        /// 이미 생성된 캐릭터의 외형을 수정(PUT /api/characters/me)한다. 닉네임은 생성 이후 변경 대상이 아니므로 전송하지 않는다.
        /// </summary>
        private async Awaitable SaveAsyncInternal(UserSaveData _saveData, Action<bool> _onComplete)
        {
            if (_saveData == null)
            {
                _onComplete?.Invoke(false);
                return;
            }

            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            var requestBody = new UpdateCustomizationRequestBody
            {
                _hairIndex = _saveData.hairIndex,
                _eyeIndex = _saveData.eyeIndex,
                _mouthIndex = _saveData.mouthIndex
            };
            string json = JsonUtility.ToJson(requestBody);
            (bool success, string _, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterMeApiPath, "PUT", json);

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"세이브 데이터 저장 실패 : {error}");
                _onComplete?.Invoke(false);
                return;
            }

            ApplyToLocalCache(_saveData);
            _onComplete?.Invoke(true);
        }

        private async Awaitable DeleteAsyncInternal(Action<bool> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            (bool success, string _, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterMeApiPath, "DELETE");

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"세이브 데이터 삭제 실패 : {error}");
                _onComplete?.Invoke(false);
                return;
            }

            ClearLocalCache();
            _onComplete?.Invoke(true);
        }

private void ApplyToLocalCache(UserSaveData _saveData)
        {
            cachedSaveData = _saveData;
            PlayerPrefs.SetString(SaveDataKey, JsonUtility.ToJson(_saveData));

            // characterCount는 서버 응답(FetchFromServerAsync)이든 생성 시 계산값(CreateNew)이든 항상 이
            // 세이브 데이터에 실린 값을 소스 오브 트루스로 삼아 로컬 카운터를 동기화한다.
            cachedCharacterCount = _saveData.characterCount;
            PlayerPrefs.SetInt(CharacterCountKey, _saveData.characterCount);

            PlayerPrefs.Save();
        }

private void ClearLocalCache()
        {
            cachedSaveData = null;
            PlayerPrefs.DeleteKey(SaveDataKey);

            int remainingCount = Math.Max(0, GetCharacterCount() - 1);
            cachedCharacterCount = remainingCount;
            PlayerPrefs.SetInt(CharacterCountKey, remainingCount);

            PlayerPrefs.Save();
        }
    }
}
