using System;
using Incheol.Models.Define;
using UnityEngine;

/// <summary>
/// 몬스터 종류 하나의 정적 정의(외형만). MonsterDatabaseSO에 리스트로 등록되어 monsterType으로 조회된다.
/// HP/공격력/방어력 등 전투 스탯은 서버(GameRoom/MonsterSpawnPointDefinition)가 유일한 출처이므로 여기서는
/// 다루지 않는다 - 서버가 보낸 GameMonsterInfo의 값을 그대로 표시만 한다.
/// </summary>
[Serializable]
public class MonsterData
{
    [Tooltip("서버 MonsterSpawnPointDefinition.MonsterType과 정확히 일치해야 한다. 예) RedBoar")]
    public string monsterType;

    public string displayName;

    [Tooltip("이 몬스터의 시각적 프리팹을 가리키는 Addressable 키.")]
    public AddressableAssetKey addressableKey = AddressableAssetKey.None;
}
