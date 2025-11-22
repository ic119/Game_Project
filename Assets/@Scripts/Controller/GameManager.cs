using JJORY.Util;
using UnityEngine;

namespace JJORY.Module
{
    public class GameManager : SingletonObject<GameManager>
    {
        #region Variable
        [Header("유저 게임 정보 관련")]
        public bool isUserData = false;
        public GameObject player_Prefab;

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

        public void GenerateMaps(string _key, GameObject _go)
        {
            cur_Map = AddressableController.Instance.InstantiatePrefabHelper<GameObject>(_key, _go.transform);
            Utils.CreateLogMessage<GameManager>($"Generated Map Object {_key}");
            //foreach (GameObject go in cur_Map.transform)
            //{
            //    if (go.tag.Equals("Spawn"))
            //    {
            //        spawn_Position = go;
            //    }
            //}
        }
        #endregion
    }
}