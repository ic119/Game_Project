using Incheol.Controller;
using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI_GameSceneView에서 분리된 몬스터 타겟 HUD. 이름/체력바 표시만 담당하며,
/// 타겟을 얼마나 유지할지(타겟-로스트 타임아웃 등)는 이 View가 아니라 Presenter(GameSceneManager)가 판단해
/// BindTarget/ClearTarget을 호출한다.
/// </summary>
public class UI_MonsterTargetView : MonoBehaviour
{
    [SerializeField] private GameObject monsterInfoContainer;
    [SerializeField] private TextMeshProUGUI monsterNameLabel;
    [SerializeField] private Slider monsterHpBarSlider;
    [SerializeField] private TextMeshProUGUI monsterHpBarSliderValue;

    private RemoteMonsterController targetMonster;

    public bool HasTarget => targetMonster != null;

    private void OnEnable()
    {
        if (monsterInfoContainer != null)
        {
            monsterInfoContainer.SetActive(false);
        }
    }

    /// <summary>
    /// 체력은 전투 중 실시간으로 바뀌므로 이벤트 대신 폴링으로 슬라이더를 갱신한다
    /// (PlayerCharacterModel 주석에 명시된 기존 컨벤션).
    /// </summary>
    private void Update()
    {
        // targetMonster가 죽어 오브젝트가 파괴되면 Unity의 오버로드된 ==로 자연히 null 취급되므로,
        // monsterInfoContainer의 활성 상태를 매 프레임 이 값 하나로 동기화한다.
        bool hasLiveTarget = HasTarget;

        if (monsterInfoContainer != null && monsterInfoContainer.activeSelf != hasLiveTarget)
        {
            monsterInfoContainer.SetActive(hasLiveTarget);
        }

        if (monsterHpBarSlider == null)
        {
            return;
        }

        if (hasLiveTarget)
        {
            monsterHpBarSlider.maxValue = Mathf.Max(1, targetMonster.MaxHp);
            monsterHpBarSlider.value = targetMonster.CurrentHp;

            if (monsterHpBarSliderValue != null)
            {
                monsterHpBarSliderValue.text = $"{targetMonster.CurrentHp}/{targetMonster.MaxHp}";
            }
        }
        else
        {
            // 타겟이 죽거나(파괴) Presenter가 ClearTarget을 호출하면 체력바/이름을 비운다.
            monsterHpBarSlider.value = 0;

            if (monsterHpBarSliderValue != null)
            {
                monsterHpBarSliderValue.text = string.Empty;
            }

            if (monsterNameLabel != null)
            {
                monsterNameLabel.text = string.Empty;
            }
        }
    }

    /// <summary>
    /// 새로 공격 대상이 된 몬스터를 바인딩한다. 이름은 등급 색상(ItemGradeUtils)을 입혀 표시하고,
    /// 체력바는 Update()에서 계속 폴링해 갱신한다.
    /// </summary>
    public void BindTarget(RemoteMonsterController _monster)
    {
        targetMonster = _monster;

        if (monsterNameLabel == null)
        {
            return;
        }

        if (targetMonster == null)
        {
            monsterNameLabel.text = string.Empty;
            return;
        }

        string colorHex = ColorUtility.ToHtmlStringRGBA(targetMonster.Grade.GetGradeColor());
        string gradeName = targetMonster.Grade.GetGradeDisplayName();
        monsterNameLabel.text = $"<color=#{colorHex}>{targetMonster.DisplayName}({gradeName})</color>";
    }

    public void ClearTarget()
    {
        BindTarget(null);
    }
}
