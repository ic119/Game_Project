using UnityEngine;

public class PlayerChaseCamera : MonoBehaviour
{
    #region Variable
    [Header("추적 대상")]
    [SerializeField] public Transform followTarget;

    [Header("거리")]
    [SerializeField] private float distance = 6f;

    [Header("마우스 휠 — 거리")]
    [Tooltip("마우스 휠 값에 곱해 거리 변화량을 조정합니다 (양수 유지).")]
    [SerializeField] private float zoomScrollSensitivity = 4f;

    [SerializeField] private float distanceMin = 1f;

    [SerializeField] private float distanceMax = 60f;

    [Header("각도 (도)")]
    [Tooltip("월드 Y축 기준 좌우 회전. 0이면 월드 정면(+Z)의 반대(-Z) 쪽에서 추적. 우클릭 드래그 조절.")]
    [SerializeField] private float yawOffsetDegrees;

    [Tooltip("위/아래 기울기. 우클릭 드래그 조절")]
    [SerializeField] private float pitchOffsetDegrees = 15f;

    [Header("우클릭 드래그 — 피치")]
    [SerializeField] private float pitchMouseSensitivity = 2f;

    [Tooltip("우클릭 드래그 시 허용하는 피치 하한")]
    [SerializeField] private float pitchMinDegrees = -85f;

    [Tooltip("우클릭 드래그 시 허용하는 피치 상한")]
    [SerializeField] private float pitchMaxDegrees = 20f;

    [Header("우클릭 드래그 — 요(Yaw)")]
    [SerializeField] private float yawMouseSensitivity = 2f;

    [Header("시선 높이")]
    [Tooltip("플레이어 원점에서 위로 올린 지점 거리.")]
    [SerializeField] private float lookAtHeightOffset = 1.5f;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        ClampPitchToLimits();
        ClampDistanceToLimits();
    }

    private void OnValidate()
    {
        ClampPitchToLimits();
        ClampDistanceToLimits();
    }

    private void LateUpdate()
    {
        Zoom();
        Rotate();
        PlayerChase();
    }
    #endregion

    #region Method
    /// <summary>
    /// Player 캐릭터 자동 추적
    /// </summary>
    private void PlayerChase()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 pivot = followTarget.position + Vector3.up * lookAtHeightOffset;

        // 월드 정면(+Z) 기준 수평 방향부터 시작 (플레이어 forward 미사용)
        Vector3 backDir = Quaternion.AngleAxis(yawOffsetDegrees, Vector3.up) * Vector3.back;
        
        Vector3 pitchAxis = Vector3.Cross(Vector3.up, backDir);
        if (pitchAxis.sqrMagnitude > 1e-6f)
        {
            pitchAxis.Normalize();
            backDir = Quaternion.AngleAxis(pitchOffsetDegrees, pitchAxis) * backDir;
        }

        if (backDir.sqrMagnitude < 1e-6f)
        {
            backDir = Vector3.back;
        }

        backDir.Normalize();

        transform.position = pivot + backDir * distance;
        transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
    }

    /// <summary>
    /// 마우스 우 드래그를 통해 카메라 상하좌우 회전
    /// </summary>
    private void Rotate()
    {
        if (Input.GetMouseButton(1))
        {
            // 마우스를 위로 움직이면(통상 Mouse Y+) 피치가 커져 더 위에서 내려다보는 구도
            pitchOffsetDegrees += Input.GetAxis("Mouse Y") * pitchMouseSensitivity;
            ClampPitchToLimits();

            // 마우스를 오른쪽으로 움직이면(통상 Mouse X+) 요가 커져 카메라가 플레이어 기준 왼쪽(반시계 방향 궤도)
            yawOffsetDegrees += Input.GetAxis("Mouse X") * yawMouseSensitivity;
        }
    }

    /// <summary>
    /// 마우스 휠 입력으로 타깃과의 거리를 조정
    /// </summary>
    private void Zoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-5f)
        {
            return;
        }

        // ScrollWheel 값이 크면 타깃에 가까워지고(+), 작아지면 멀어지게(-) 맞춤
        distance -= scroll * zoomScrollSensitivity;
        ClampDistanceToLimits();
    }

    /// <summary>
    /// 상하 y축 제한 값 예외처리
    /// </summary>
    private void ClampPitchToLimits()
    {
        float low = Mathf.Min(pitchMinDegrees, pitchMaxDegrees);
        float high = Mathf.Max(pitchMinDegrees, pitchMaxDegrees);
        pitchOffsetDegrees = Mathf.Clamp(pitchOffsetDegrees, low, high);
    }

    /// <summary>
    /// Zoom 실행 후 최소~최대 범위 내에서 적용되도록 처리
    /// </summary>
    private void ClampDistanceToLimits()
    {
        float low = Mathf.Min(distanceMin, distanceMax);
        float high = Mathf.Max(distanceMin, distanceMax);
        distance = Mathf.Clamp(distance, low, high);
    }
    #endregion
}
