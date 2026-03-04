using JJORY.Module;
using UnityEngine;

namespace JJORY.Model.Player
{
    /// <summary>
    /// Player의 능력치(= 스테이터스)에 대한 클래스 정의
    /// </summary>
    public class PlayerStat
    {
        [Tooltip("Player 캐릭터의 물리 공격력에 대한 스테이터스")]
        [SerializeField] private int strength = 1;
        [Tooltip("Player 캐릭터의 마법 공격력 및 캐스팅 속도에 대한 스테이터스")]
        [SerializeField] private int intellect = 1;
        [Tooltip("Player 캐릭터의 이동속도 & 공격속도에 대한 스테이터스")]
        [SerializeField] private int agility = 1;
        [Tooltip("Player 캐릭터의 체력 수치 및 방어력에 대한 스테이터스")]
        [SerializeField] private int healthy = 1;

        public int Strength { get { return strength; } }
        public int Intellect { get { return intellect; } }
        public int Agility { get { return agility; } }
        public int Healthy { get { return healthy; } }

        /// <summary>
        /// 처음 시작하거나 새 게임을 할 때 캐릭터의 능력치 초기화 처리
        /// </summary>
        public void Init()
        {
            strength = 1;
            intellect = 1;
            agility = 1;
            healthy = 1;
        }

        /// <summary>
        /// 스테이지 클리어 or 유저가 죽을 때 or 게임종료 시 캐릭터의 데이터를 업데이트 후 로컬저장 처리
        /// </summary>
        public void UpdateUserStat()
        {

        }

        /// <summary>
        /// 유저가 스테이지 클리어 이후 유저의 Stat을 +1씩 증가
        /// </summary>
        public void UpgradeUserStat(PlayerStat _curStat)
        {
            if (_curStat == null)
            {
                return;
            }

            // 현재 능력치를 기준으로 각각 +1 증가
            strength = _curStat.Strength + 1;
            intellect = _curStat.Intellect + 1;
            agility = _curStat.Agility + 1;
            healthy = _curStat.Healthy + 1;
        }
    }

    /// <summary>
    /// 캐릭터의 특수 능력 (= 개성)에 대한 클래스 정의
    /// </summary>
    public class PlayerIndividuality
    {
        [Header("캐릭터의 개성 여부")]
        private bool hasIndividuality = false;

        public class IndividualityGrade
        {

        }
    }

    /// <summary>
    /// Player의 게임 운영에 대한 정보 클래스
    /// </summary>
    public class PlayerGameInfo
    {
        [Tooltip("현재 유저가 속한 Stage의 정보")]
        [SerializeField] private int curStage = 0;

        public int CurStage { get { return curStage; } }
    }

    public class PlayerModel : MonoBehaviour
    {
        #region Variable
        public PlayerStat playerStat = new PlayerStat();
        public PlayerGameInfo playerGameInfo = new PlayerGameInfo();
        #endregion

        #region LifeCylce
        private void Start()
        {
            // 게임 시작 시 GameManager를 통해 첫 시작 여부 체크 및 스탯 초기화
            GameManager.Instance.Init(this);
        }
        #endregion

        #region Method

        #endregion
    }
}
