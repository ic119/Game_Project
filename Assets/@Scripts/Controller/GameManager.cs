using JJORY.Util;
using UnityEngine;

namespace JJORY.Module
{
    public class GameManager : SingletonObject<GameManager>
    {
        #region Variable
        [Header("유저 게임 정보 관련")]
        public bool isUserData = false;
        public GameObject player;

        [Header("Map")]
        public GameObject cur_Map;
        public GameObject spawn_Position;
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

        /// <summary>
        /// 월드 맵 생성 로직
        /// </summary>
        /// <param name="_key"></param>
        /// <param name="_go"></param>
        public void GenerateMaps(string _key, GameObject _go)
        {
            cur_Map = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(_key, _go.transform);
            Utils.CreateLogMessage<GameManager>($"Generated Map Object {_key}");

            if (cur_Map != null)
            {
                foreach (Transform tr in cur_Map.transform)
                {
                   if (tr.gameObject.tag.Equals("Spawn"))
                    {
                        spawn_Position = tr.gameObject;
                        Utils.CreateLogMessage<GameManager>($"플레이어 캐릭터 스폰 위치 세팅완료");
                    }
                }
            }
            else
            {
                Utils.CreateLogMessage<GameManager>("cur_Map is Null");
            }
        }
        #endregion
    }
}