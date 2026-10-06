using System;
using System.Collections.Generic;
using UnityEngine;
using Incheol.Models.Define;

namespace Incheol.Controller
{
    /// <summary>
    /// 스폰 포인트 하나에서 나올 수 있는 몬스터 타입 하나. 서버 GameServer.Monsters.MonsterSpawnEntry와
    /// 형식이 동일해야 한다 - 스폰/리스폰마다 소속 포인트의 entries 중 하나가 weight 비율로 선택된다.
    /// 스탯/AI 값은 여기에 없다 - 서버 Monsters/MonsterDefinitions.json이 몬스터 타입별로 들고 있다.
    /// </summary>
    [Serializable]
    public class MonsterSpawnEntry
    {
        [Tooltip("인스펙터에서 몬스터 타입을 선택한다. Define.MonsterType과 1:1로 대응하며, " +
            "서버 MonsterDefinitions.json에 같은 이름의 정의가 있어야 한다.")]
        public MonsterType monsterType = MonsterType.None;

        [Tooltip("선택 가중치(상대 비율). 모두 1이면 균등 랜덤이고, 3과 1이면 75%/25%다.")]
        [Min(1)] public int weight = 1;
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
        [Tooltip("이 포인트에서 나올 수 있는 몬스터 타입들. 스폰/리스폰마다 weight 비율로 이 중 하나를 골라 " +
            "그 타입의 서버 정의(MonsterDefinitions.json)를 그대로 적용한다(서버 GameRoom.SpawnMonsterAtPoint). " +
            "최소 1개 이상 있어야 한다.")]
        public List<MonsterSpawnEntry> entries = new();

        [Header("개체수/리스폰")]
        [Min(1)] public int maxAlive = 1;
        [Min(0f)] public float respawnSeconds = 20f;

        [Header("활동 영역 (던전 방처럼 벽으로 나뉜 곳에서만 사용)")]
        [Tooltip("켜면 이 포인트의 몬스터는 아래 영역 안의 플레이어만 감지/추적하고 영역 밖으로 나가지 않으며, 영역 안의 " +
            "가구/기둥/벽(콜라이더)은 돌아서 이동한다(내보낼 때 이동 격자 NavGrids/{맵}.json이 함께 만들어진다). " +
            "이때 이 마커의 Y 높이를 방 바닥 높이로 쓰므로 마커를 바닥에 두어야 한다. 트인 필드는 끈다.")]
        public bool confineToArea;

        [Tooltip("영역 중심의 월드 XZ 좌표(마커 위치와 같은 좌표계). 마커 자신이 영역 안에 있어야 한다.")]
        public Vector2 areaCenter;

        [Tooltip("영역 전체 크기(X, Z). 둘 다 0보다 커야 한다.")]
        public Vector2 areaSize = new Vector2(8f, 8f);

        // 씬 뷰에서 활동 영역(녹색)을 사각형으로 보여준다. 선택된 상태에서만 그려서 마커가 많아져도
        // 씬 뷰가 도형으로 뒤덮이지 않게 한다.
        private void OnDrawGizmosSelected()
        {
            if (!confineToArea)
            {
                return;
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(areaCenter.x, transform.position.y, areaCenter.y), new Vector3(areaSize.x, 0.1f, areaSize.y));
        }
    }
}
