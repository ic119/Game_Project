using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UI 3D 프리뷰 영역에서 마우스 드래그를 감지하여 회전 델타를 전달하는 이벤트 핸들러
/// </summary>
public class UI_DragRotateHandler : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerDownHandler
{
    #region Variable
    public event Action<float> OnRotateDelta;
    public event Action OnDragStarted;
    public event Action OnDragEnded;

    [SerializeField] private float dragMultiplier = 1.0f;
    #endregion

    #region Method
    public void OnPointerDown(PointerEventData eventData)
    {
        // 클릭 시 포커스 및 상호작용 준비
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        OnDragStarted?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        float deltaX = eventData.delta.x * dragMultiplier;
        OnRotateDelta?.Invoke(deltaX);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        OnDragEnded?.Invoke();
    }
    #endregion
}
