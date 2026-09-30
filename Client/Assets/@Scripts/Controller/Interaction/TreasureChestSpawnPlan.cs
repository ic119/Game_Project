using System;
using System.Collections.Generic;
using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 등급(lootTableKey) 하나당 "후보 중 몇 개에 상자를 세울지". 서버가 방 생성 시 같은 lootTableKey를 가진
    /// 후보(TreasureChestSpawnPointMarker) 중에서 count개를 무작위로 뽑는다.
    /// </summary>
    [Serializable]
    public class TreasureChestSpawnCount
    {
        public ChestLootTableKey lootTableKey = ChestLootTableKey.TreasureChestBasic;
        [Min(0)] public int count = 1;
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
