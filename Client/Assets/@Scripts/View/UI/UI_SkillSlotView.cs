using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    /// <summary>
    /// 스킬 슬롯 하나(UI_GameScene/BottomContainer/SkillContainer/SkillSlot00N)의 표시 전용 뷰. 어떤 스킬인지/쿨다운이 얼마나 남았는지 같은
    /// 판단은 UI_SkillBarView가 하고, 이 뷰는 넘겨받은 값을 그리기만 한다.
    /// 상태 표시: 레벨 잠금(어둡게 + 해금 레벨), 마나 부족(어둡게 + 마나 비용 빨간색), 쿨다운 중(채움 오버레이 + 남은 초), 사용 가능.
    /// 아이콘은 아직 에셋이 없어 비워 두고, 스프라이트가 정해지면 SetIcon으로 넣는다.
    /// </summary>
    public class UI_SkillSlotView : MonoBehaviour
    {
        #region Variable
        [Header("기본 표시")]
        [Tooltip("스킬 아이콘. 스프라이트가 없으면 숨긴다.")]
        [SerializeField] private Image iconImage;

        [Tooltip("슬롯에 대응하는 키 번호(\"1\" 등).")]
        [SerializeField] private TextMeshProUGUI keyLabel;

        [Tooltip("마나 비용. 마나가 모자라면 insufficientManaColor로 바뀐다.")]
        [SerializeField] private TextMeshProUGUI manaCostLabel;

        [Header("상태 표시")]
        [Tooltip("쿨다운 채움 오버레이. Image Type을 Filled로 두면 남은 비율만큼 채워진다.")]
        [SerializeField] private Image cooldownOverlay;

        [SerializeField] private TextMeshProUGUI cooldownLabel;

        [Tooltip("레벨 잠금/마나 부족일 때 슬롯을 어둡게 덮는 이미지.")]
        [SerializeField] private Image dimOverlay;

        [Tooltip("레벨 잠금 표시의 루트. 켜져 있는 동안 lockLevelLabel에 해금 레벨이 표시된다.")]
        [SerializeField] private GameObject lockRoot;

        [SerializeField] private TextMeshProUGUI lockLevelLabel;

        [Tooltip("시전 시작 때 잠깐 깜빡이는 이미지. 비워 두면 하이라이트를 건너뛴다.")]
        [SerializeField] private Image highlightImage;

        [Header("색상/연출")]
        [SerializeField] private Color enoughManaColor = new Color(0.55f, 0.8f, 1f, 1f);
        [SerializeField] private Color insufficientManaColor = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.6f;
        [SerializeField, Min(0.01f)] private float highlightSeconds = 0.25f;

        // 마지막으로 그린 값. 같은 값이면 문자열을 다시 만들지 않는다(매 프레임 폴링되므로 GC를 피한다).
        private int shownManaCost = -1;
        private int shownLockLevel = -1;
        private int shownCooldownSecondsTimes10 = -1;
        private float highlightEndTime;
        #endregion

        #region LifeCycle
        private void Update()
        {
            if (highlightImage == null || !highlightImage.enabled)
            {
                return;
            }

            float remain = highlightEndTime - Time.unscaledTime;
            if (remain <= 0f)
            {
                highlightImage.enabled = false;
                return;
            }

            Color color = highlightImage.color;
            color.a = remain / highlightSeconds;
            highlightImage.color = color;
        }
        #endregion

        #region Method
        /// <summary>키 번호 문구를 지정한다. 프리팹의 기본 문구를 그대로 쓰려면 호출하지 않는다.</summary>
        public void SetKeyText(string text)
        {
            if (keyLabel != null)
            {
                keyLabel.text = text;
            }
        }

        /// <summary>아이콘 스프라이트를 지정한다. null이면 아이콘을 숨긴다.</summary>
        public void SetIcon(Sprite sprite)
        {
            if (iconImage == null)
            {
                return;
            }

            iconImage.sprite = sprite;
            iconImage.enabled = sprite != null;
        }

        /// <summary>
        /// 슬롯 상태를 그린다. skill이 null이면 이 무기에 이 슬롯 스킬이 없는 것이라 모든 표시를 끈다.
        /// </summary>
        /// <param name="isLocked">플레이어 레벨이 해금 레벨보다 낮은지.</param>
        /// <param name="hasEnoughMana">현재 마나가 마나 비용 이상인지.</param>
        public void Refresh(SkillTable.Entry? skill, bool isLocked, bool hasEnoughMana, float cooldownRemaining, float cooldownDuration)
        {
            if (skill == null)
            {
                SetActive(manaCostLabel, false);
                SetActive(lockRoot, false);
                SetCooldown(0f, 0f);
                SetDim(false);
                return;
            }

            SkillTable.Entry entry = skill.Value;

            RefreshLock(entry.UnlockLevel, isLocked);
            RefreshManaCost(entry.ManaCost, hasEnoughMana, isLocked);
            SetDim(isLocked || !hasEnoughMana);

            // 잠긴 슬롯은 쿨다운이 흐르지 않는다(해금 전에는 쓸 수 없으므로 남아 있어도 표시하지 않는다).
            SetCooldown(isLocked ? 0f : cooldownRemaining, cooldownDuration);
        }

        /// <summary>시전이 시작될 때 슬롯을 잠깐 밝게 깜빡인다.</summary>
        public void Flash()
        {
            if (highlightImage == null)
            {
                return;
            }

            highlightEndTime = Time.unscaledTime + highlightSeconds;
            Color color = highlightImage.color;
            color.a = 1f;
            highlightImage.color = color;
            highlightImage.enabled = true;
        }

        private void RefreshLock(int unlockLevel, bool isLocked)
        {
            SetActive(lockRoot, isLocked);

            if (isLocked && lockLevelLabel != null && shownLockLevel != unlockLevel)
            {
                shownLockLevel = unlockLevel;
                lockLevelLabel.text = $"Lv.{unlockLevel}";
            }
        }

        private void RefreshManaCost(int manaCost, bool hasEnoughMana, bool isLocked)
        {
            // 잠긴 슬롯은 잠금 표시가 대신하므로 마나 비용은 숨긴다.
            SetActive(manaCostLabel, !isLocked);

            if (manaCostLabel == null || isLocked)
            {
                return;
            }

            if (shownManaCost != manaCost)
            {
                shownManaCost = manaCost;
                manaCostLabel.text = manaCost.ToString();
            }

            manaCostLabel.color = hasEnoughMana ? enoughManaColor : insufficientManaColor;
        }

        private void SetCooldown(float remaining, float duration)
        {
            bool cooling = remaining > 0f && duration > 0f;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.enabled = cooling;
                if (cooling)
                {
                    cooldownOverlay.fillAmount = Mathf.Clamp01(remaining / duration);
                }
            }

            if (cooldownLabel == null)
            {
                return;
            }

            cooldownLabel.enabled = cooling;
            if (!cooling)
            {
                shownCooldownSecondsTimes10 = -1;
                return;
            }

            // 1초 이상은 정수 올림("5"), 1초 미만은 소수 첫째 자리("0.7")로 보인다. 표시가 바뀔 때만 문자열을 만든다.
            int key = remaining >= 1f ? Mathf.CeilToInt(remaining) * 10 : Mathf.CeilToInt(remaining * 10f);
            if (key == shownCooldownSecondsTimes10)
            {
                return;
            }

            shownCooldownSecondsTimes10 = key;
            cooldownLabel.text = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : (key / 10f).ToString("0.0");
        }

        private void SetDim(bool dim)
        {
            if (dimOverlay == null)
            {
                return;
            }

            dimOverlay.enabled = dim;
            if (dim)
            {
                Color color = dimOverlay.color;
                color.a = dimAlpha;
                dimOverlay.color = color;
            }
        }

        private static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
            {
                target.gameObject.SetActive(active);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
        #endregion
    }
}
