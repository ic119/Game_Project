using JJORY.Util;
using UnityEngine;

namespace JJORY.Module
{
    public class GameManager : SingletonObject<GameManager>
    {
        #region Variable
        [Header("User GameData")]
        public const string userSaveData = "UserData";
        #endregion

        #region Method
        public void Init()
        {

        }

        private void CheckUserSaveData()
        {
            if (PlayerPrefs.HasKey(userSaveData))
            {
                string userData = PlayerPrefs.GetString(userSaveData);
                Debug.Log($"Loaded UserData from PlayerPrefs: {userData}");
            }
        }
        #endregion
    }
}
