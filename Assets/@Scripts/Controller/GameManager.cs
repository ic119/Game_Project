using JJORY.Util;
using UnityEngine;

namespace JJORY.Module
{
    public class GameManager : SingletonObject<GameManager>
    {
        #region Variable
        [Header("유저 게임 정보 관련")]
        public bool isUserData = false;
        #endregion

        #region Method
        public void Init() 
        {
            CheckSaveData("UserData");
        }

        private void CheckSaveData(string _key)
        {
            if (PlayerPrefs.HasKey(_key))
            {
                int value = PlayerPrefs.GetInt(_key);
                if (value == 1)
                {
                    isUserData = true;
                }
                else
                {
                    isUserData = false;
                }
            }
            else
            {
                Utils.CreateLogMessage<GameManager>("저장된 게임 정보가 없습니다.");
            }
        }
        #endregion
    }
}