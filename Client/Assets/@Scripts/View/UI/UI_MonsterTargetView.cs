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
    private bool wasLiveTarget;

    public bool HasTarget => targetMonster != null;

    private void OnEnable()
    {
        if (monsterInfoContainer != null)
        {
            monsterInfoContainer.SetActive(false);
        }

        wasLiveTarget = false;

        // 비활성화되는 동안 구독을 해제했으므로, 타겟이 남아 있으면 다시 구독하고 현재 체력으로 맞춘다
        // (비활성 중 BindTarget이 이미 구독했을 수 있어 -= 후 += 한다).
        if (targetMonster != null)
        {
            targetMonster.OnHpChanged -= RefreshHp;
            targetMonster.OnHpChanged += RefreshHp;
            RefreshHp(targetMonster.CurrentHp, targetMonster.MaxHp);
            return;
        }

        ResetDisplay();
    }

    private void OnDisable()
    {
        UnsubscribeTarget();
    }

    /// <summary>
    /// 체력은 몬스터의 OnHpChanged 이벤트로만 갱신한다(RefreshHp) - 매 프레임 슬라이더/문자열을 다시 만들지 않는다.
    /// 여기서는 이벤트로 알 수 없는 "타겟 몬스터 오브젝트가 파괴됨(사망 연출 후 제거, 시야 이탈, 맵 전환)"만 감지한다.
    /// 파괴되면 Unity의 오버로드된 ==로 targetMonster가 null 취급되므로, 컨테이너 활성 상태를 이 값 하나로 동기화한다.
    /// </summary>
    private void Update()
    {
        bool hasLiveTarget = HasTarget;

        if (monsterInfoContainer != null && monsterInfoContainer.activeSelf != hasLiveTarget)
        {
            monsterInfoContainer.SetActive(hasLiveTarget);
        }

        // 타겟이 파괴되어 사라진 프레임에 한 번만 체력바/이름을 비운다.
        if (wasLiveTarget && !hasLiveTarget)
        {
            ResetDisplay();
        }

        wasLiveTarget = hasLiveTarget;
    }

    /// <summary>
    /// 새로 공격 대상이 된 몬스터를 바인딩한다. 이름은 등급 색상(ItemGradeUtils)을 입혀 표시하고,
    /// 체력바는 몬스터의 OnHpChanged 구독으로 갱신한다. 같은 몬스터를 다시 바인딩하면(공격할 때마다 호출됨)
    /// 구독은 그대로 두고 표시만 다시 맞춘다.
    /// </summary>
    public void BindTarget(RemoteMonsterController _monster)
    {
        if (!ReferenceEquals(targetMonster, _monster))
        {
            UnsubscribeTarget();
            targetMonster = _monster;

            if (targetMonster != null)
            {
                targetMonster.OnHpChanged += RefreshHp;
            }
        }

        if (targetMonster != null)
        {
            RefreshHp(targetMonster.CurrentHp, targetMonster.MaxHp);
        }

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

    private void RefreshHp(int currentHp, int maxHp)
    {
        if (monsterHpBarSlider != null)
        {
            monsterHpBarSlider.maxValue = Mathf.Max(1, maxHp);
            monsterHpBarSlider.value = currentHp;
        }

        if (monsterHpBarSliderValue != null)
        {
            monsterHpBarSliderValue.text = $"{currentHp}/{maxHp}";
        }
    }

    private void ResetDisplay()
    {
        if (monsterHpBarSlider != null)
        {
            monsterHpBarSlider.value = 0;
        }

        if (monsterHpBarSliderValue != null)
        {
            monsterHpBarSliderValue.text = string.Empty;
        }

        if (monsterNameLabel != null)
        {
            monsterNameLabel.text = string.Empty;
        }
    }

    // 파괴된 몬스터는 Unity ==로는 null이지만 C# 객체는 남아 있으므로 `is null`로 판단한다(이벤트 필드는 접근해도 안전하다).
    private void UnsubscribeTarget()
    {
        if (targetMonster is not null)
        {
            targetMonster.OnHpChanged -= RefreshHp;
        }
    }
}
