using System;
using Incheol.Models.Define;
using UnityEngine;

/// <summary>
/// 몬스터 종류 하나의 정적 정의. MonsterDatabaseSO에 리스트로 등록되어 monsterType으로 조회된다.
/// grade/maxHp/attackPower는 도감·툴팁 등 UI 표시용 참고값이다 - 실제 전투에 쓰이는 HP/공격력/방어력의
/// 유일한 출처는 서버(GameRoom/MonsterSpawnPointDefinition)이며, 런타임 값은 서버가 보낸 GameMonsterInfo를
/// 그대로 표시한다. 이 값을 바꿔도 서버 쪽 스폰 정의를 함께 바꾸지 않으면 실제 전투 결과에는 반영되지 않는다.
/// </summary>
[Serializable]
public class MonsterData
{
    [Tooltip("서버 MonsterSpawnPointDefinition.MonsterType과 정확히 일치해야 한다. 예) RedBoar")]
    public string monsterType;

    public string displayName;

    [Tooltip("이 몬스터의 시각적 프리팹을 가리키는 Addressable 키.")]
    public AddressableAssetKey addressableKey = AddressableAssetKey.None;

    [Tooltip("몬스터 등급(표시용). ItemGrade를 그대로 재사용한다 - 등급 색상(ItemGradeUtils.GetGradeColor) 등 " +
        "기존 등급 표현 로직을 몬스터 UI(네임플레이트/도감 등)에도 그대로 적용할 수 있다.")]
    public ItemGrade grade = ItemGrade.Common;

    [Tooltip("도감/툴팁 등에 표시할 참고용 기본 체력. 실제 전투에서 쓰이는 MaxHp는 서버 " +
        "MonsterSpawnPointDefinition.MaxHp가 유일한 출처이며 이 값과 독립적이다.")]
    public int maxHp;

    [Tooltip("도감/툴팁 등에 표시할 참고용 기본 공격력. 실제 전투에서 쓰이는 AttackPower는 서버 " +
        "MonsterSpawnPointDefinition.AttackPower가 유일한 출처이며 이 값과 독립적이다.")]
    public int attackPower;
}
