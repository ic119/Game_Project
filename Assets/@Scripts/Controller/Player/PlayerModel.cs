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

        }

        /// <summary>
        /// 스테이지 클리어 or 유저가 죽을 때 or 게임종료 시 캐릭터의 데이터를 업데이트 후 로컬저장 처리
        /// </summary>
        public void UpdateUserStat()
        {

        }
    }

    /// <summary>
    /// Player의 게임 운영에 대한 정보 클래스
    /// </summary>
    public class PlayerGameInfo
    {
        [Tooltip("Player 캐릭터의 물리 공격력에 대한 스테이터스")]
        [SerializeField] private int curStage = 0;

        public int CurStage { get { return curStage; } }
    }

    public class PlayerMove
    {
        [SerializeField] private PlayerMoveState moveState = PlayerMoveState.Idle;
        public PlayerMoveState MoveState { get { return moveState; } }
    }

    public class PlayerModel : MonoBehaviour
    {
        #region Variable
        public PlayerStat playerState = new PlayerStat();
        public PlayerMove playerMove = new PlayerMove();
        #endregion

        #region Method
        #endregion
    }
}
