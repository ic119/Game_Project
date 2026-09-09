using Incheol.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 계정이 보유한 캐릭터 목록/개별 캐릭터를 서버(GET api/characters, GET/PUT/DELETE api/characters/{id},
    /// POST api/characters)와 동기화하는 매니저. 로비에서 "선택된 캐릭터"(SelectedCharacterId)를 로컬 상태로 들고 있으며,
    /// Update/Delete는 별도 id 인자 없이 이 선택 상태를 대상으로 동작한다.
    /// </summary>
    public class SaveDataManager : SingletonObject<SaveDataManager>
    {
        private const string CharacterApiPath = "/api/characters";
        private const string SelectedCharacterIdKey = "SelectedCharacterId";

        protected override bool PersistAcrossScenes => true;

        private long? selectedCharacterId;
        private CharacterSummary selectedCharacter;

        /// <summary>
        /// 캐릭터 생성 요청이 실제로 서버 응답을 받아 끝났을 때 발생(성공/실패 모두). UI가 낙관적 반환(CreateNew)이 아니라
        /// 실제 완료 시점에 목록을 갱신하고 싶을 때 이 이벤트를 구독한다.
        /// </summary>
        public event Action<bool> OnCharacterCreateResult;

        // 서버 DTO(MainServer.CharacterServer.DTOs)와 필드명을 맞춘 전송 전용 바디.
        // UserSaveData(클라이언트 내부 모델)와 서버 계약을 분리하기 위해 네트워크 경계에서만 사용한다.
        [Serializable] private class CreateCharacterRequestBody { public string _nickname; public int _hairIndex; public int _eyeIndex; public int _mouthIndex; }
        [Serializable] private class UpdateCustomizationRequestBody { public int _hairIndex; public int _eyeIndex; public int _mouthIndex; }
        [Serializable] private class CharacterResponseBody { public long _id; public string _nickname; public int _hairIndex; public int _eyeIndex; public int _mouthIndex; public int _str; public int _agi; public int _intel; public int _level; public string _lastLoginAt; public string _createdAt; }
        [Serializable] private class JsonArrayWrapper<T> { public T[] items; }

        /// <summary>
        /// 로비에서 선택된 캐릭터의 ID. 최초 접근 시 PlayerPrefs에서 지연 로드된다.
        /// </summary>
        public long? SelectedCharacterId => selectedCharacterId ??= LoadSelectedCharacterIdFromPrefs();

        /// <summary>
        /// 선택된 캐릭터의 목록 표시용 요약 데이터(메모리 캐시). 앱을 재시작한 직후처럼 아직 목록을 못 받아온 경우 null일 수 있다 -
        /// 이 경우 FetchCharacterListAsync가 완료되면 SelectedCharacterId와 매칭해 자동으로 채워진다.
        /// </summary>
        public CharacterSummary SelectedCharacter => selectedCharacter;

        public bool HasSelectedCharacter => SelectedCharacterId.HasValue;

        /// <summary>
        /// 로비 캐릭터 목록에서 캐릭터를 선택(클릭)했을 때 호출한다. 다음 세션에도 유지되도록 PlayerPrefs에 저장한다.
        /// </summary>
        public void SelectCharacter(CharacterSummary _character)
        {
            if (_character == null)
            {
                return;
            }

            selectedCharacterId = _character.id;
            selectedCharacter = _character;
            PlayerPrefs.SetString(SelectedCharacterIdKey, _character.id.ToString());
            PlayerPrefs.Save();
        }

        public void ClearSelection()
        {
            selectedCharacterId = null;
            selectedCharacter = null;
            PlayerPrefs.DeleteKey(SelectedCharacterIdKey);
            PlayerPrefs.Save();
        }

        private static long? LoadSelectedCharacterIdFromPrefs()
        {
            string raw = PlayerPrefs.GetString(SelectedCharacterIdKey, string.Empty);
            return long.TryParse(raw, out long id) ? id : (long?)null;
        }

        /// <summary>
        /// 계정이 보유한 캐릭터 전체 목록을 서버에서 조회한다(GET api/characters, 배열 응답).
        /// 완료되면 SelectedCharacterId와 매칭되는 요약을 자동으로 캐시하고, 선택된 캐릭터가 목록에 없으면(다른 기기에서 삭제 등) 선택을 해제한다.
        /// </summary>
        public void FetchCharacterListAsync(Action<List<CharacterSummary>> _onComplete)
        {
            _ = FetchCharacterListAsyncInternal(_onComplete);
        }


        /// <summary>
        /// 특정 캐릭터의 상세 데이터(헤어/눈/입 포함)를 서버에서 조회한다(GET api/characters/{characterId}).
        /// 로비에서 선택된 캐릭터의 실제 외형을 미리보기(3D)에 적용할 때 사용한다.
        /// </summary>
        public void FetchCharacterDetailAsync(long _characterId, Action<UserSaveData> _onComplete)
        {
            _ = FetchCharacterDetailAsyncInternal(_characterId, _onComplete);
        }


        /// <summary>
        /// 캐릭터를 신규 생성한다(POST api/characters). UI_CharacterCreatePopup.OnCreateRequested(Func&lt;UserSaveData, bool&gt;)가
        /// 동기 반환을 요구하므로 여기서는 요청 접수만 확인하고, 서버 저장은 백그라운드로 진행한다.
        /// 실제 완료(성공/실패)는 OnCharacterCreateResult 이벤트로 알린다. 성공 시 새로 만든 캐릭터가 자동으로 선택된다.
        /// </summary>
        public bool CreateNew(UserSaveData _saveData)
        {
            if (_saveData == null)
            {
                return false;
            }

            _ = CreateAsyncInternal(_saveData);
            return true;
        }

        /// <summary>
        /// 선택된 캐릭터의 외형(헤어/눈/입)을 서버에 저장(PUT api/characters/{id})한다.
        /// 선택된 캐릭터가 없으면 실패로 처리한다.
        /// </summary>
        public void UpdateCharacterCustomization(int _hairIndex, int _eyeIndex, int _mouthIndex, Action<bool> _onComplete = null)
        {
            if (!SelectedCharacterId.HasValue)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("선택된 캐릭터가 없어 외형을 갱신할 수 없습니다.");
                _onComplete?.Invoke(false);
                return;
            }

            _ = UpdateCustomizationAsyncInternal(SelectedCharacterId.Value, _hairIndex, _eyeIndex, _mouthIndex, _onComplete);
        }

        /// <summary>
        /// 선택된 캐릭터를 서버에서 삭제(DELETE api/characters/{id})하고, 성공했을 때만 선택 상태를 해제한다.
        /// </summary>
        public void DeleteAsync(Action<bool> _onComplete = null)
        {
            if (!SelectedCharacterId.HasValue)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("선택된 캐릭터가 없어 삭제할 수 없습니다.");
                _onComplete?.Invoke(false);
                return;
            }

            _ = DeleteAsyncInternal(SelectedCharacterId.Value, _onComplete);
        }

        private async Awaitable FetchCharacterListAsyncInternal(Action<List<CharacterSummary>> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(null);
                return;
            }

            (bool success, string body, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterApiPath, "GET");

            if (!success || string.IsNullOrEmpty(body))
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 목록 조회 실패 : {error}");
                _onComplete?.Invoke(null);
                return;
            }

            CharacterResponseBody[] responses = ParseJsonArray<CharacterResponseBody>(body);
            if (responses == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 목록 응답 파싱에 실패했습니다 : {body}");
                _onComplete?.Invoke(null);
                return;
            }

            List<CharacterSummary> summaries = new List<CharacterSummary>(responses.Length);
            for (int i = 0; i < responses.Length; i++)
            {
                summaries.Add(ToSummary(responses[i]));
            }

            SyncSelectionWithFetchedList(summaries);

            _onComplete?.Invoke(summaries);
        }


        private async Awaitable FetchCharacterDetailAsyncInternal(long _characterId, Action<UserSaveData> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(null);
                return;
            }

            (bool success, string body, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync($"{CharacterApiPath}/{_characterId}", "GET");

            if (!success || string.IsNullOrEmpty(body))
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 상세 조회 실패 : {error}");
                _onComplete?.Invoke(null);
                return;
            }

            CharacterResponseBody response = JsonUtility.FromJson<CharacterResponseBody>(body);
            if (response == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 상세 응답 파싱에 실패했습니다 : {body}");
                _onComplete?.Invoke(null);
                return;
            }

            _onComplete?.Invoke(new UserSaveData
            {
                characterId = response._id,
                nickname = response._nickname,
                hairIndex = response._hairIndex,
                eyeIndex = response._eyeIndex,
                mouthIndex = response._mouthIndex
            });
        }


        private void SyncSelectionWithFetchedList(List<CharacterSummary> _summaries)
        {
            if (!SelectedCharacterId.HasValue)
            {
                return;
            }

            CharacterSummary match = _summaries.Find(c => c.id == SelectedCharacterId.Value);
            if (match == null)
            {
                // 선택돼 있던 캐릭터가 서버 목록에 더 이상 없다(다른 기기에서 삭제 등) - 선택을 해제한다.
                ClearSelection();
                return;
            }

            selectedCharacter = match;
        }

        /// <summary>
        /// 캐릭터를 최초 생성(POST api/characters)한다. 이미 슬롯이 가득 찬 계정이면 서버가 409를 반환한다.
        /// </summary>
        private async Awaitable CreateAsyncInternal(UserSaveData _saveData)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                OnCharacterCreateResult?.Invoke(false);
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
            (bool success, string body, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync(CharacterApiPath, "POST", json);

            if (!success || string.IsNullOrEmpty(body))
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 생성 저장 실패 : {error}");
                OnCharacterCreateResult?.Invoke(false);
                return;
            }

            CharacterResponseBody response = JsonUtility.FromJson<CharacterResponseBody>(body);
            if (response == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 생성 응답 파싱에 실패했습니다 : {body}");
                OnCharacterCreateResult?.Invoke(false);
                return;
            }

            // 방금 생성한 캐릭터를 자동으로 선택 상태로 만든다.
            SelectCharacter(ToSummary(response));
            OnCharacterCreateResult?.Invoke(true);
        }

        /// <summary>
        /// 특정 캐릭터의 외형을 수정(PUT api/characters/{characterId})한다. 닉네임은 생성 이후 변경 대상이 아니므로 전송하지 않는다.
        /// </summary>
        private async Awaitable UpdateCustomizationAsyncInternal(long _characterId, int _hairIndex, int _eyeIndex, int _mouthIndex, Action<bool> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            var requestBody = new UpdateCustomizationRequestBody
            {
                _hairIndex = _hairIndex,
                _eyeIndex = _eyeIndex,
                _mouthIndex = _mouthIndex
            };
            string json = JsonUtility.ToJson(requestBody);
            (bool success, string _, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync($"{CharacterApiPath}/{_characterId}", "PUT", json);

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 외형 저장 실패 : {error}");
                _onComplete?.Invoke(false);
                return;
            }

            _onComplete?.Invoke(true);
        }

        private async Awaitable DeleteAsyncInternal(long _characterId, Action<bool> _onComplete)
        {
            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>("ServerConnectManager.Instance가 null입니다.");
                _onComplete?.Invoke(false);
                return;
            }

            (bool success, string _, string error) = await ServerConnectManager.Instance.SendAuthorizedJsonRequestAsync($"{CharacterApiPath}/{_characterId}", "DELETE");

            if (!success)
            {
                DebugLogManager.GenerateErrorMessage<SaveDataManager>($"캐릭터 삭제 실패 : {error}");
                _onComplete?.Invoke(false);
                return;
            }

            if (SelectedCharacterId.HasValue && SelectedCharacterId.Value == _characterId)
            {
                ClearSelection();
            }

            _onComplete?.Invoke(true);
        }

        private static CharacterSummary ToSummary(CharacterResponseBody _response)
        {
            return new CharacterSummary
            {
                id = _response._id,
                nickname = _response._nickname,
                level = _response._level,
                lastLoginAt = _response._lastLoginAt
            };
        }

        // JsonUtility는 최상위 JSON 배열을 직접 파싱하지 못하므로 래퍼 객체로 감싸서 처리한다.
        private static T[] ParseJsonArray<T>(string _json)
        {
            try
            {
                string wrapped = "{\"items\":" + _json + "}";
                JsonArrayWrapper<T> wrapper = JsonUtility.FromJson<JsonArrayWrapper<T>>(wrapped);
                return wrapper?.items;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
