using System;
using System.Collections.Generic;
using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 등급(lootTableKey) 하나당 "후보 중 몇 개에 상자를 세울지". 서버가 방 생성 시 같은 lootTableKey를 가진
    /// 후보(TreasureChestSpawnPointMarker) 중에서 count개를 무작위로 뽑는다. 상자를 연 뒤에는 열린 상자가
    /// despawnDelaySeconds 뒤에 사라지고, respawnSeconds가 되면 같은 등급의 다른 후보 지점에 새 상자가 생긴다
    /// (등급별 개수가 유지된다).
    /// </summary>
    [Serializable]
    public class TreasureChestSpawnCount
    {
        public ChestLootTableKey lootTableKey = ChestLootTableKey.TreasureChestBasic;
        [Min(0)] public int count = 1;

        [Tooltip("상자를 연 뒤 새 상자가 다른 후보 지점에 다시 생기기까지의 시간(초). 0이면 리스폰하지 않는다.")]
        [Min(0f)] public float respawnSeconds = 180f;

        [Tooltip("연 상자가 열린 채로 남아 있다가 사라지기까지의 시간(초). 리스폰을 켜려면 1초 이상, respawnSeconds 이하여야 한다.")]
        [Min(0f)] public float despawnDelaySeconds = 20f;
    }

    /// <summary>
    /// 맵 프리팹 루트에 하나 두는 에디터 전용 설정. TreasureChestSpawnPointMarker 후보들 중 등급별로 몇 개를
    /// 활성화할지만 들고 있고, 런타임에는 아무 동작도 하지 않는다(MapDataExporter가 chestSpawnCounts[]로 내보낸다).
    /// 후보가 있는데 이 컴포넌트가 없거나 count가 후보 수보다 크면 내보내기가 중단된다.
    /// </summary>
    public class TreasureChestSpawnPlan : MonoBehaviour
    {
        public List<TreasureChestSpawnCount> counts = new();
    }
}
