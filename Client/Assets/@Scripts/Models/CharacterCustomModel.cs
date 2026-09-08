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
                hairCustomList[i].SetActive(i == index);
            }
        }
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
