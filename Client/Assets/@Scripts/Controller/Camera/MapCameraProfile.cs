using UnityEngine;

namespace Incheol.Controller
{
    /// <summary>
    /// 맵 프리팹 루트에 하나 두는 카메라 시점 설정. 맵이 생성(활성화)되면 CameraOrbitController에 이 맵의 시점을 적용한다.
    /// 맵 이동은 이전 맵을 지우고 새 맵을 만드는 방식이라(GameSceneManager.SwapMap), 새 맵의 OnEnable이 곧 시점 전환 시점이다.
    /// 새 맵을 추가할 때 SwapMap 코드는 건드릴 필요 없이 이 컴포넌트만 붙이면 된다.
    /// </summary>
    public class MapCameraProfile : MonoBehaviour
    {
        #region Variable
        [Tooltip("CinemachineFollow의 월드 기준 오프셋(플레이어로부터 카메라까지). 높이/거리로 내려다보는 각도가 정해진다.")]
        [SerializeField] private Vector3 followOffset = new Vector3(-3.5355341f, 3.9f, -3.5355337f);

        [Tooltip("켜면 마우스 우클릭 드래그로 카메라를 수평 회전할 수 있다. 탑다운 고정 시점이면 끈다.")]
        [SerializeField] private bool allowRotation = true;
        #endregion

        #region LifeCycle
        private void OnEnable()
        {
            if (CameraOrbitController.Instance == null)
            {
                Debug.LogWarning($"{name}: CameraOrbitController가 없어 맵 카메라 시점을 적용하지 못했습니다.");
                return;
            }

            CameraOrbitController.Instance.ApplyProfile(followOffset, allowRotation);
        }
        #endregion
    }
}
