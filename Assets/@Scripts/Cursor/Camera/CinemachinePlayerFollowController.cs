using JJORY.Module;
using JJORY.Util;
using Unity.Cinemachine;
using UnityEngine;


public class CinemachinePlayerFollowController : MonoBehaviour
{
	#region Variable
	[Header("Target")]
	public Transform target;

	[Header("Follow Settings")]
	[SerializeField] private Vector3 followOffset = new Vector3(0f, 4f, 1.5f);
	[SerializeField, Range(0f, 5f)] private float positionDamping = 0.5f;
	[SerializeField, Range(0f, 5f)] private float rotationDamping = 0.5f;

	[SerializeField] private CinemachineCamera cmCamera;
	[SerializeField] private CinemachinePositionComposer positionComposer;

	[Header("Orbit (Right Click)")]
	[SerializeField] private float orbitSensitivityX = 3f;
	[SerializeField] private float orbitSensitivityY = 2f;
	[SerializeField, Range(-89f, 0f)] private float orbitPitchMin = -60f;
	[SerializeField, Range(0f, 89f)] private float orbitPitchMax = 60f;

	private Transform _orbitPivot;
	private Transform _cameraSlot;
	private float _orbitYaw;
	private float _orbitPitch;
	#endregion

	#region LifeCycle
	private void Awake()
	{
		EnsureCinemachineBrainOnMainCamera();
		AutoFindTarget();
		SetupCinemachine();
		ApplySettings();
	}   

	private void Update()
	{
		if (Input.GetMouseButton(1))
		{
			_orbitYaw += Input.GetAxis("Mouse X") * orbitSensitivityX;
			_orbitPitch -= Input.GetAxis("Mouse Y") * orbitSensitivityY;
			_orbitPitch = Mathf.Clamp(_orbitPitch, orbitPitchMin, orbitPitchMax);
		}

		if (_orbitPivot != null)
			_orbitPivot.localRotation = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
	}

	private void OnValidate()
	{
		ApplySettings();
	}
	#endregion

	#region Method
	/// <summary>
	/// MainCamera에 CinemachineBrain이 없을 경우 자동 추가 처리
	/// </summary>
	private void EnsureCinemachineBrainOnMainCamera()
	{
		Camera mainCam = Camera.main;
		if (mainCam == null)
		{
			return;
		}

		if (mainCam.GetComponent<CinemachineBrain>() == null)
		{
			mainCam.gameObject.AddComponent<CinemachineBrain>();
		}
	}

    /// <summary>
    /// 카메라가 추적할 대상을 PhysicsPlayerController컴포넌트를 가진 오브젝트를 탐지 후 자동 추가 처리
    /// </summary>
    private void AutoFindTarget()
	{
		if (target != null)
		{
			return;
		}
        PhysicsPlayerController player = FindFirstObjectByType<PhysicsPlayerController>();
		if (player != null)
		{
			target = player.transform;
		}
	}

	/// <summary>
	/// 시네머신 카메라 설정 세팅
	/// </summary>
	private void SetupCinemachine()
	{
		if (cmCamera == null)
		{
			cmCamera = GetComponent<CinemachineCamera>();
			if (cmCamera == null)
			{
				cmCamera = gameObject.AddComponent<CinemachineCamera>();
			}
		}

		positionComposer = cmCamera.GetComponent<CinemachinePositionComposer>();
		if (positionComposer == null)
		{
			positionComposer = cmCamera.gameObject.AddComponent<CinemachinePositionComposer>();
		}

		CreateOrbitRig();
		cmCamera.Follow = _cameraSlot != null ? _cameraSlot : target;
		cmCamera.LookAt = target;
	}

	/// <summary>
	/// 우클릭 오빗 회전용 피벗/슬롯 생성
	/// </summary>
	private void CreateOrbitRig()
	{
		if (target == null) return;

		if (_orbitPivot != null)
		{
			if (Application.isPlaying)
				Destroy(_orbitPivot.gameObject);
			else
				DestroyImmediate(_orbitPivot.gameObject);
			_orbitPivot = null;
			_cameraSlot = null;
		}

		float distance = positionComposer != null && positionComposer.CameraDistance > 0.01f
			? positionComposer.CameraDistance
			: Mathf.Max(0.1f, new Vector2(followOffset.x, followOffset.z).magnitude);

		var pivotGo = new GameObject("OrbitPivot");
		pivotGo.transform.SetParent(target);
		pivotGo.transform.localPosition = new Vector3(0f, followOffset.y, 0f);
		pivotGo.transform.localRotation = Quaternion.identity;
		_orbitPivot = pivotGo.transform;

		var slotGo = new GameObject("CameraSlot");
		slotGo.transform.SetParent(_orbitPivot);
		slotGo.transform.localPosition = new Vector3(0f, 0f, -distance);
		slotGo.transform.localRotation = Quaternion.identity;
		_cameraSlot = slotGo.transform;
	}

	/// <summary>
	/// Cinemachine Camera 속성 세팅 처리
	/// </summary>
	private void ApplySettings()
	{
		if (positionComposer != null)
		{
			positionComposer.TargetOffset = new Vector3(0f, followOffset.y, 0f);
			positionComposer.CameraDistance = Mathf.Max(0.1f, new Vector2(followOffset.x, followOffset.z).magnitude);
			positionComposer.Damping = new Vector3(positionDamping, positionDamping, positionDamping);
		}

		if (_orbitPivot != null)
			_orbitPivot.localPosition = new Vector3(0f, followOffset.y, 0f);
		if (_cameraSlot != null && positionComposer != null)
			_cameraSlot.localPosition = new Vector3(0f, 0f, -positionComposer.CameraDistance);
	}

	/// <param name="_target"></param>
	public void SetTarget(Transform _target)	
	{
		target = _target;
		SetupCinemachine();
		ApplySettings();
	}
	#endregion
}