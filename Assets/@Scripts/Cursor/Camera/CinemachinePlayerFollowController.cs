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
	//[SerializeField] private CinemachineRotationComposer rotationComposer;
	#endregion

	#region LifeCycle
	private void Awake()
	{
		EnsureCinemachineBrainOnMainCamera();
		AutoFindTarget();
		SetupCinemachine();
		ApplySettings();
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

		cmCamera.Follow = target;
		cmCamera.LookAt = target;

		positionComposer = cmCamera.GetComponent<CinemachinePositionComposer>();
		if (positionComposer == null)
		{
			positionComposer = cmCamera.gameObject.AddComponent<CinemachinePositionComposer>();
		}

		//rotationComposer = cmCamera.GetComponent<CinemachineRotationComposer>();
		//if (rotationComposer == null)
		//{
		//	rotationComposer = cmCamera.gameObject.AddComponent<CinemachineRotationComposer>();
		//}
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
		//if (rotationComposer != null)
		//{
		//	rotationComposer.Damping = new Vector3(rotationDamping, rotationDamping, rotationDamping);
		//}
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