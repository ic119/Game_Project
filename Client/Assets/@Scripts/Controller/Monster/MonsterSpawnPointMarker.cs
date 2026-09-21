using System;
using System.Collections.Generic;
using UnityEngine;
using Incheol.Models.Define;

namespace Incheol.Controller
{
    /// <summary>
    /// 스폰 포인트 하나에서 나올 수 있는 몬스터 타입 하나. 서버 GameServer.Monsters.MonsterSpawnEntry와
    /// 형식이 동일해야 한다 - 스폰/리스폰마다 소속 포인트의 entries 중 이 항목이 무작위로 선택될 수 있다.
    /// </summary>
    [Serializable]
    public class MonsterSpawnEntry
    {
        [Tooltip("인스펙터에서 몬스터 타입을 선택한다. Define.MonsterType과 1:1로 대응한다.")]
        public MonsterType monsterType = MonsterType.None;

        [Header("스탯 (실제 전투에 쓰이는 유일한 출처 - 서버로 그대로 전달된다)")]
        [Min(1)] public int maxHp = 30;
        [Min(0)] public int attackPower = 5;
        [Min(0)] public int defense = 0;
        [Min(0)] public int expReward = 20;
    }

    /// <summary>
    /// 몬스터 스폰 포인트를 맵 프리팹 안에 시각적으로 배치하기 위한 에디터 전용 마커.
    /// 런타임에는 아무 동작도 하지 않는다 - 실제 스폰 판단/실행은 여전히 서버(GameRoom)의 권한이며,
    /// 이 컴포넌트가 들고 있는 값은 별도의 내보내기 도구(Tools/Monster/Export Spawn Points)가
    /// SpawnPoints/{mapId}.json으로 직렬화해 서버에 넘겨줄 데이터일 뿐이다.
    /// 좌표/회전(PointId 포함)은 GameObject의 이름과 Transform을 그대로 쓰고 이 컴포넌트에는
    /// 따로 필드를 두지 않는다 - 두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문이다.
    /// </summary>
    public class MonsterSpawnPointMarker : MonoBehaviour
    {
        [Tooltip("이 포인트에서 나올 수 있는 몬스터 타입들. 스폰/리스폰마다 이 중 하나를 무작위로 골라 " +
            "그 타입의 스탯을 그대로 적용한다(서버 GameRoom.SpawnMonsterAtPoint). 최소 1개 이상 있어야 한다.")]
        public List<MonsterSpawnEntry> entries = new();

        [Header("개체수/리스폰")]
        [Min(1)] public int maxAlive = 1;
        [Min(0f)] public float respawnSeconds = 20f;

        [Header("인식/추적 AI")]
        [Tooltip("이 거리 안에 플레이어가 들어오면 추적을 시작한다.")]
        [Min(0f)] public float detectionRange = 6f;
        [Tooltip("추적/복귀 중 초당 이동 거리.")]
        [Min(0f)] public float chaseSpeed = 2.5f;
        [Tooltip("스폰 지점으로부터 이 거리 이상 벗어나면 추적을 포기하고 복귀한다.")]
        [Min(0f)] public float leashRange = 10f;

        // 씬 뷰에서 감지 범위(노랑)/리쉬 범위(빨강)를 원으로 보여준다. 선택된 상태에서만 그려서
        // 마커가 많아져도 씬 뷰가 원으로 뒤덮이지 않게 한다.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, leashRange);
        }
    }
}
