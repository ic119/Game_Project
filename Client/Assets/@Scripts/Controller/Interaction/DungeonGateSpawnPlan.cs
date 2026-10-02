using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Controller.Interaction
{
    /// <summary>
    /// 맵 프리팹 루트에 하나 두는 던전 게이트 설정. DungeonGateSpawnPointMarker 후보 중 서버가 한 곳만 뽑아 그 자리에 게이트
    /// (Addressables "PortalGate")를 세운다. 게이트가 어느 맵으로 가는지를 여기서 정하고, 클라이언트는 게이트를 만든 직후
    /// MapPortalController.SetTargetMap으로 이 값을 넘긴다. MapDataExporter는 같은 값을 gatePlan으로 내보내
    /// 서버가 뽑힌 후보를 MapSwap 포탈로 검증하게 한다 - 클라이언트와 서버가 한 곳의 설정을 보므로 어긋나지 않는다.
    /// 후보가 있는데 이 컴포넌트가 없거나 도착 맵이 None이면 내보내기가 중단된다.
    /// </summary>
    public class DungeonGateSpawnPlan : MonoBehaviour
    {
        [Tooltip("게이트로 이동할 도착 맵의 Addressable 키(MapPortalController.targetMapKey와 같은 의미).")]
        public AddressableAssetKey targetMapKey = AddressableAssetKey.None;

        [Tooltip("도착 맵 안에서 플레이어를 배치할 진입 지점 Transform의 이름(MapPortalController.targetMapEntryPointName과 같은 의미).")]
        public string targetMapEntryPointName = "RespawnPoint";
    }
}
