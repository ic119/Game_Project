using UnityEngine;

namespace JJORY.Model.Player
{
    public class PlayerStat
    {
        [SerializeField] private int strength = 10;
        [SerializeField] private int intellect = 10;
        [SerializeField] private int agility = 10;

        public int Strength { get { return strength; } }
        public int Intellect { get { return intellect; } }
        public int Agility { get { return agility; } }

    }

    public class PlayerMove
    {
        [SerializeField] private PlayerMoveState moveState = PlayerMoveState.Idle;
        public PlayerMoveState MoveStata { get { return moveState; } }
    }
    public class PlayerModel : MonoBehaviour
    {
        #region Variable
        #endregion
    }
}