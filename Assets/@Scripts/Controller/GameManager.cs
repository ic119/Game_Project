using JJORY.Model.Player;
using JJORY.Util;
using UnityEngine;

namespace JJORY.Module
{
    public class GameManager : SingletonObject<GameManager>
    {
        #region Variable
        [Header("User GameData")]
        public const string userSaveData = "UserData";

        [Header("Player Reference")]
        // 씬에 생성된 플레이어 캐릭터(PlayerPrefab 인스턴스)를 보관하는 변수
        private PlayerModel currentPlayer;
        public PlayerModel CurrentPlayer => currentPlayer;

        /// <summary>
        /// 로그인 성공 시 저장되는 계정명. 씬 전환 후 PlayerPrefab 생성 시 사용한다.
        /// </summary>
        private static string loggedInUserName;
        public static string LoggedInUserName => loggedInUserName;
        #endregion

        #region Method
        /// <summary>
        /// 로그인 UI에서 입력한 계정명을 씬 전환까지 보관한다.
        /// </summary>
        public static void SetLoggedInUserName(string _userName)
        {
            loggedInUserName = _userName ?? string.Empty;
        }

        /// <summary>
        /// 게임 시작 시 유저 데이터 존재 여부를 확인하고,
        /// 첫 게임이면 플레이어 능력치를 초기화한다.
        /// </summary>
        public void Init(PlayerModel playerModel)
        {
            // 어떤 상황에서도 Player에 접근할 수 있도록 참조를 저장
            currentPlayer = playerModel;

            // 저장 데이터가 이미 있다면 → 기존 데이터 로드(추후 구현)
            if (PlayerPrefs.HasKey(userSaveData))
            {
                string userData = PlayerPrefs.GetString(userSaveData);
                Debug.Log($"Loaded UserData from PlayerPrefs: {userData}");

                // TODO: userData를 파싱해서 playerModel.playerStat에 반영하는 로직
                return;
            }

            // 저장 데이터가 없다면 → 첫 게임 시작
            Debug.Log("First game start. Init player stat to 1.");

            if (playerModel != null)
            {
                playerModel.playerStat.Init();
            }

            // 초기화된 상태를 바로 저장
            SaveUserData(playerModel);
        }

        /// <summary>
        /// 현재 플레이어의 능력치와 스테이지 정보를 문자열로 만들어
        /// PlayerPrefs에 저장한다.
        /// </summary>
        public void SaveUserData(PlayerModel playerModel)
        {
            if (playerModel == null || playerModel.playerStat == null || playerModel.playerGameInfo == null)
            {
                Debug.LogWarning("SaveUserData: PlayerModel or its data is null.");
                return;
            }

            string userData =
                $"Str={playerModel.playerStat.Strength};" +
                $"Int={playerModel.playerStat.Intellect};" +
                $"Agi={playerModel.playerStat.Agility};" +
                $"Hp={playerModel.playerStat.Healthy};" +
                $"Stage={playerModel.playerGameInfo.CurStage}";

            PlayerPrefs.SetString(userSaveData, userData);
            PlayerPrefs.Save();

            Debug.Log($"UserData saved to PlayerPrefs: {userData}");
        }
        #endregion
    }
}
