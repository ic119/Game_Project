using Incheol.Utils;
using UnityEngine;

namespace Incheol.Modules
{
    /// <summary>
    /// 유저의 캐릭터 세이브 데이터를 저장/조회하는 매니저.
    /// </summary>
    public class SaveDataManager : SingletonObject<SaveDataManager>
    {
        private const string SaveDataKey = "UserSaveData";

        protected override bool PersistAcrossScenes => true;

        private UserSaveData cachedSaveData;

        public bool HasSaveData => PlayerPrefs.HasKey(SaveDataKey);

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

        public bool Save(UserSaveData _saveData)
        {
            if (_saveData == null)
            {
                return false;
            }

            cachedSaveData = _saveData;
            PlayerPrefs.SetString(SaveDataKey, JsonUtility.ToJson(_saveData));
            PlayerPrefs.Save();
            return true;
        }

        public bool UpdateCharacterCustomization(int _hairIndex, int _eyeIndex, int _mouthIndex)
        {
            UserSaveData current = Load();
            if (current == null)
            {
                return false;
            }

            current.hairIndex = _hairIndex;
            current.eyeIndex = _eyeIndex;
            current.mouthIndex = _mouthIndex;
            return Save(current);
        }
    }
}
