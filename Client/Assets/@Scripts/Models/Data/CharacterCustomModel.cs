using System.Collections.Generic;
using UnityEngine;

public class CharacterCustomModel : MonoBehaviour
{
    #region Variable
    [Header("헤어 스타일 커스텀 목록")]
    [SerializeField] private List<GameObject> hairCustomList = new List<GameObject>();

    [Header("눈 스타일 커스텀 목록")]
    [SerializeField] private List<GameObject> eyeCustomList = new List<GameObject>();

    [Header("입 스타일 커스텀 목록")]
    [SerializeField] private List<GameObject> mouthCustomList = new List<GameObject>();

    public int HairCount => hairCustomList != null ? hairCustomList.Count : 0;
    public int EyeCount => eyeCustomList != null ? eyeCustomList.Count : 0;
    public int MouthCount => mouthCustomList != null ? mouthCustomList.Count : 0;

    public int CurrentHairIndex { get; private set; } = 0;
    public int CurrentEyeIndex { get; private set; } = 0;
    public int CurrentMouthIndex { get; private set; } = 0;

    /// <summary>
    /// true면 선택된 헤어를 화면에 켜지 않는다(투구를 쓰면 헤어가 투구를 뚫고 나오기 때문 - EquipmentController가 설정한다).
    /// 선택한 헤어 인덱스(CurrentHairIndex)는 그대로 기억하므로, 숨김을 풀면 원래 헤어가 다시 켜진다.
    /// 켜짐 상태를 이 클래스가 계속 소유하므로 Awake/ApplyCustomization/SetHair가 나중에 호출돼도 숨김이 풀리지 않는다.
    /// </summary>
    public bool IsHairHidden { get; private set; }
    #endregion

    #region LifeCycle
    private void Awake()
    {
        ApplyCustomization(CurrentHairIndex, CurrentEyeIndex, CurrentMouthIndex);
    }
    #endregion

    #region Method
    public void ApplyCustomization(int hairIndex, int eyeIndex, int mouthIndex)
    {
        SetHair(hairIndex);
        SetEye(eyeIndex);
        SetMouth(mouthIndex);
    }

    public void SetHair(int index)
    {
        if (hairCustomList == null || hairCustomList.Count == 0) return;
        if (index < 0) index = hairCustomList.Count - 1;
        if (index >= hairCustomList.Count) index = 0;

        CurrentHairIndex = index;
        for (int i = 0; i < hairCustomList.Count; i++)
        {
            if (hairCustomList[i] != null)
            {
                hairCustomList[i].SetActive(!IsHairHidden && i == index);
            }
        }
    }

    /// <summary>
    /// 헤어 숨김 상태를 바꾸고 현재 선택된 헤어를 즉시 다시 반영한다.
    /// </summary>
    public void SetHairHidden(bool hidden)
    {
        if (IsHairHidden == hidden)
        {
            return;
        }

        IsHairHidden = hidden;
        SetHair(CurrentHairIndex);
    }

    public void SetEye(int index)
    {
        if (eyeCustomList == null || eyeCustomList.Count == 0) return;
        if (index < 0) index = eyeCustomList.Count - 1;
        if (index >= eyeCustomList.Count) index = 0;

        CurrentEyeIndex = index;
        for (int i = 0; i < eyeCustomList.Count; i++)
        {
            if (eyeCustomList[i] != null)
            {
                eyeCustomList[i].SetActive(i == index);
            }
        }
    }

    public void SetMouth(int index)
    {
        if (mouthCustomList == null || mouthCustomList.Count == 0) return;
        if (index < 0) index = mouthCustomList.Count - 1;
        if (index >= mouthCustomList.Count) index = 0;

        CurrentMouthIndex = index;
        for (int i = 0; i < mouthCustomList.Count; i++)
        {
            if (mouthCustomList[i] != null)
            {
                mouthCustomList[i].SetActive(i == index);
            }
        }
    }
    #endregion
}
