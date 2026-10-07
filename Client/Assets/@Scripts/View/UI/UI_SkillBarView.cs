using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules.Networking;
using Incheol.Utils;
using TMPro;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// UI_GameScene/BottomContainer/SkillContainer에 붙는 스킬 바. 슬롯 4개(키 1~4)를 묶어 로컬 플레이어의 레벨/마나/무기/쿨다운을
    /// 매 프레임 폴링해 각 UI_SkillSlotView에 넘긴다(UI_GameSceneView와 같은 폴링 컨벤션). 장착 무기에 스킬이 없으면 슬롯을 모두 숨긴다.
    /// PlayerSkillController.SkillCast로 시전한 슬롯을 깜빡이고, SkillRejected로 서버 거부 사유를 짧게 안내한다.
    /// 이 컴포넌트가 붙은 오브젝트 자체는 끄지 않는다(Update가 멈추므로) - 슬롯 오브젝트만 켜고 끈다.
    /// </summary>
    public class UI_SkillBarView : MonoBehaviour
    {
        #region Variable
        [Tooltip("슬롯 1~4 순서대로 SkillSlot001~004의 UI_SkillSlotView를 연결한다.")]
        [SerializeField] private UI_SkillSlotView[] slots = new UI_SkillSlotView[SkillTable.SlotCount];

        [Header("거부 안내")]
        [Tooltip("서버가 시전을 거부했을 때 잠깐 띄우는 문구. 비워 두면 안내를 건너뛴다.")]
        [SerializeField] private TextMeshProUGUI messageLabel;

        [SerializeField, Min(0.1f)] private float messageSeconds = 1.5f;

        private PlayerCharacterModel playerModel;
        private PlayerSkillController skillController;
        private bool slotsVisible = true;
        private float messageHideTime;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (messageLabel != null)
            {
                messageLabel.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Update()
        {
            UpdateMessage();

            if (playerModel == null || skillController == null)
            {
                SetSlotsVisible(false);
                return;
            }

            int weaponIndex = (int)playerModel.CurrentWeaponType;
            bool hasSkills = SkillTable.HasSkills(weaponIndex);
            SetSlotsVisible(hasSkills);

            if (!hasSkills)
            {
                return;
            }

            int level = playerModel.Level;
            int currentMp = playerModel.CurrentMp;

            for (int i = 0; i < slots.Length && i < SkillTable.SlotCount; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                int slot = i + 1;
                SkillTable.Entry? skill = SkillTable.TryGet(weaponIndex, slot, out SkillTable.Entry entry) ? entry : null;
                bool isLocked = skill != null && level < skill.Value.UnlockLevel;
                bool hasEnoughMana = skill == null || currentMp >= skill.Value.ManaCost;

                slots[i].Refresh(skill, isLocked, hasEnoughMana, skillController.GetCooldownRemaining(slot), skillController.GetCooldownDuration(slot));
            }
        }
        #endregion

        #region Method
        /// <summary>
        /// 로컬 플레이어를 연결한다. 이미 다른 플레이어가 연결돼 있으면 먼저 해제한다.
        /// </summary>
        public void Bind(PlayerCharacterModel model, PlayerSkillController controller)
        {
            Unbind();

            playerModel = model;
            skillController = controller;

            if (skillController != null)
            {
                skillController.SkillCast += HandleSkillCast;
                skillController.SkillRejected += HandleSkillRejected;
            }
        }

        private void Unbind()
        {
            if (skillController != null)
            {
                skillController.SkillCast -= HandleSkillCast;
                skillController.SkillRejected -= HandleSkillRejected;
            }

            playerModel = null;
            skillController = null;
        }

        private void HandleSkillCast(int slot, SkillTable.Entry skill)
        {
            if (slot >= 1 && slot <= slots.Length && slots[slot - 1] != null)
            {
                slots[slot - 1].Flash();
            }
        }

        private void HandleSkillRejected(int slot, GameSkillCastStatus status)
        {
            // 시전 잠금(Casting)은 연타하면 자연스럽게 생기고, 사망/잘못된 요청은 플레이어가 할 일이 없으므로 안내하지 않는다.
            string message = status switch
            {
                GameSkillCastStatus.NotEnoughMana => "마나가 부족합니다",
                GameSkillCastStatus.LevelTooLow => "아직 사용할 수 없는 스킬입니다",
                GameSkillCastStatus.OnCooldown => "재사용 대기 중입니다",
                GameSkillCastStatus.NoSkill => "사용할 수 없는 스킬입니다",
                _ => null,
            };

            if (message == null || messageLabel == null)
            {
                return;
            }

            messageLabel.text = message;
            messageLabel.gameObject.SetActive(true);
            messageHideTime = Time.unscaledTime + messageSeconds;
        }

        private void UpdateMessage()
        {
            if (messageLabel != null && messageLabel.gameObject.activeSelf && Time.unscaledTime >= messageHideTime)
            {
                messageLabel.gameObject.SetActive(false);
            }
        }

        private void SetSlotsVisible(bool visible)
        {
            if (slotsVisible == visible)
            {
                return;
            }

            slotsVisible = visible;

            foreach (UI_SkillSlotView slotView in slots)
            {
                if (slotView != null)
                {
                    slotView.gameObject.SetActive(visible);
                }
            }
        }
        #endregion
    }
}
