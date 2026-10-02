using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 던전 게이트가 나타날 수 있는 "후보 지점" 하나를 맵 프리팹 안에 시각적으로 배치하기 위한 에디터 전용 마커.
    /// 런타임에는 아무 동작도 하지 않는다 - 후보 중 어디에 게이트를 실제로 세울지는 서버가 방을 만들 때 한 곳만 정하고,
    /// 이 컴포넌트는 MapDataExporter가 MapData/{mapId}.json의 gateCandidates[]로 직렬화해 서버에 넘겨줄 위치 데이터일 뿐이다
    /// (TreasureChestSpawnPointMarker와 같은 역할 분담).
    /// 후보 id와 좌표는 GameObject의 이름과 Transform을 그대로 쓰고 이 컴포넌트에는 따로 필드를 두지 않는다 -
    /// 두 곳에서 좌표를 관리하면 서로 어긋날 수 있기 때문이다. 이름은 맵 안에서 고유해야 한다.
    /// 게이트의 도착 맵은 후보마다 따로 두지 않고 맵 프리팹 루트의 DungeonGateSpawnPlan 하나로 정한다(후보 중 한 곳에만 서므로).
    /// </summary>
    public class DungeonGateSpawnPointMarker : MonoBehaviour
    {
        // 씬 뷰에서 후보 위치(보라)와 게이트가 바라보는 방향을 보여준다. 마커의 forward가 게이트의 정면이다.
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Vector3 center = transform.position + Vector3.up * 1.5f;
            Gizmos.DrawWireCube(center, new Vector3(2f, 3f, 0.5f));
            Gizmos.DrawLine(center, center + transform.forward * 1.5f);
        }
    }
}
