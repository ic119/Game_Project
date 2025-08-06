using UnityEngine;


namespace JJory.Model.Character
{
    /// <summary>
    /// 플레이어의 캐릭터에 대한 데이터 및 상태 정의 클래스
    /// </summary>
    public class CharacterModel
    {
        #region 기본 수치 => 스탯 포인트에 따라 변경 처리
        /// <summary>
        /// 캐릭터의 기본 체력
        /// </summary>
        public int HealthPoint { get; private set; } = 100;

        /// <summary>
        /// 캐릭터의 기본 물리공격력
        /// </summary>
        public int AttackPower { get; private set; } = 10;

        /// <summary>
        /// 캐릭터의 기본 마법공격력
        /// </summary>
        public int ManaAttackPower { get; private set; } = 10;

        /// <summary>
        /// 캐릭터의 기본 방어력
        /// </summary>
        public int DefensivePower { get; private set; } = 5;

        /// <summary>
        /// 캐릭터의 기본 이동 속도
        /// </summary>
        public int MoveSpeed { get; private set; } = 10;
        #endregion

        #region 기본 스탯 포인트
        /// <summary>
        /// 물리공격력에 반영되는 스탯
        /// </summary>
        public int StrengthPoint { get; private set; } = 1;
        
        /// <summary>
        /// 이동속도 / 공격속도 / 회피에 반영되는 스탯
        /// </summary>
        public int AgilityPoint { get; private set; } = 1;

        /// <summary>
        /// 마법공격력에 반영되는 스탯
        /// </summary>
        public int IntelligencePoint { get; private set; } = 1;
        #endregion

        public CharacterModel()
        {

        }

        #region Method
        public void Init(int _str = 1, int _agl = 1, int _int = 1)
        {

        }
        #endregion
    }
}