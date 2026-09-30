using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 보물상자가 나타날 수 있는 "후보 지점" 하나를 맵 프리팹 안에 시각적으로 배치하기 위한 에디터 전용 마커.
    /// 런타임에는 아무 동작도 하지 않는다 - 후보 중 어디에 상자를 실제로 세울지는 서버가 정하고, 이 컴포넌트의 값은
    /// MapDataExporter가 MapData/{mapId}.json의 chestCandidates[]로 직렬화해 서버에 넘겨줄 데이터일 뿐이다
    /// (MonsterSpawnPointMarker와 같은 역할 분담).
    /// 후보 id와 좌표는 GameObject의 이름과 Transform을 그대로 쓰고 이 컴포넌트에는 따로 필드를 두지 않는다 -
    /// 두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문이다. 이름은 맵 안에서 고유해야 한다.
    /// </summary>
    public class TreasureChestSpawnPointMarker : MonoBehaviour
    {
        [Tooltip("이 후보에 상자가 서면 쓸 Drops/DropTables.json 키(Define.ChestLootTableKey). 같은 키끼리 한 등급(티어)으로 묶여 " +
            "TreasureChestSpawnPlan의 뽑을 개수 계산에 쓰인다.")]
        public ChestLootTableKey lootTableKey = ChestLootTableKey.TreasureChestBasic;

        [Tooltip("서버가 개봉 사거리를 판정할 때 쓰는 수평 반경(m). 상자 프리팹의 UI_InteractionPrompt 콜라이더 반경과 맞춘다.")]
        [Min(0.1f)] public float interactRadius = 1.5f;

        // 씬 뷰에서 후보 위치(청록)와 개봉 사거리를 보여준다. 선택된 상태에서만 사거리를 그려 마커가 많아도 화면이 어지럽지 않게 한다.
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.25f, new Vector3(0.5f, 0.5f, 0.5f));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
