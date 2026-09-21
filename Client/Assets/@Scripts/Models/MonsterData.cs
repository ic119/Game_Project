using System;
using Incheol.Models.Define;
using UnityEngine;

/// <summary>
/// 몬스터 종류 하나의 정적 정의. MonsterDatabaseSO에 리스트로 등록되어 monsterType으로 조회된다.
/// 외형/식별 정보만 담당한다 - HP/공격력/방어력 등 전투 스탯은 다루지 않는다. 전투 스탯의 유일한 출처는
/// 서버(GameRoom/MonsterSpawnPointDefinition, 클라이언트 쪽 MonsterSpawnEntry가 내보낸 값)이며,
/// 런타임에 스탯을 표시해야 하면 서버가 보낸 GameMonsterInfo를 그대로 쓴다 - 여기에 따로 스탯을 두면
/// 두 값이 어긋날 수 있어(이 값을 바꿔도 서버 쪽 스폰 정의를 함께 바꾸지 않으면 반영되지 않는다) 의도적으로 제외했다.
/// </summary>
[Serializable]
public class MonsterData
{
    [Tooltip("인스펙터에서 몬스터 타입을 선택한다. Define.MonsterType과 1:1로 대응한다. 이 값의 " +
        "ToString()이 그대로 Addressable Address로 쓰인다(예: RedMushroom) - 프리팹을 Addressable " +
        "그룹에 등록할 때 Address를 이 이름과 정확히 같게 맞춰야 한다.")]
    public MonsterType monsterType = MonsterType.None;

    public string displayName;

    [Tooltip("몬스터 등급(표시용). ItemGrade를 그대로 재사용한다 - 등급 색상(ItemGradeUtils.GetGradeColor) 등 " +
        "기존 등급 표현 로직을 몬스터 UI(네임플레이트/도감 등)에도 그대로 적용할 수 있다.")]
    public ItemGrade grade = ItemGrade.Common;
}
