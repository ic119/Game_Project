using System;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public enum InventoryTabType
    {
        All = 0,
        Equipment = 1,
        Consumable = 2,
        Etc = 3
    }

    /// <summary>
    /// 인벤토리 카테고리 탭 4개. 버튼 클릭을 탭 선택 콜백으로 연결하고, 선택된 탭의 버튼 색을 칠한다.
    /// 버튼 오브젝트는 UI_InventoryView의 인스펙터에서 연결된 것을 받아 쓰며, 연결되지 않은 버튼(null)은 건너뛴다.
    /// </summary>
    public class InventoryTabBar
    {
        private static readonly Color ActiveColor = new Color(0.20f, 0.65f, 1.00f, 1.00f);
        private static readonly Color InactiveColor = new Color(0.18f, 0.24f, 0.35f, 0.95f);

        private readonly Button allButton;
        private readonly Button equipmentButton;
        private readonly Button consumableButton;
        private readonly Button etcButton;

        public InventoryTabBar(Button allButton, Button equipmentButton, Button consumableButton, Button etcButton)
        {
            this.allButton = allButton;
            this.equipmentButton = equipmentButton;
            this.consumableButton = consumableButton;
            this.etcButton = etcButton;
        }

        /// <summary>버튼 클릭을 연결한다. 클릭하면 해당 탭으로 onTabClicked를 호출한다.</summary>
        public void Register(Action<InventoryTabType> onTabClicked)
        {
            AddListener(allButton, InventoryTabType.All, onTabClicked);
            AddListener(equipmentButton, InventoryTabType.Equipment, onTabClicked);
            AddListener(consumableButton, InventoryTabType.Consumable, onTabClicked);
            AddListener(etcButton, InventoryTabType.Etc, onTabClicked);
        }

        public void Unregister()
        {
            RemoveListeners(allButton);
            RemoveListeners(equipmentButton);
            RemoveListeners(consumableButton);
            RemoveListeners(etcButton);
        }

        /// <summary>선택된 탭은 강조색, 나머지는 기본색으로 칠한다.</summary>
        public void ShowSelected(InventoryTabType selected)
        {
            SetButtonColor(allButton, selected == InventoryTabType.All);
            SetButtonColor(equipmentButton, selected == InventoryTabType.Equipment);
            SetButtonColor(consumableButton, selected == InventoryTabType.Consumable);
            SetButtonColor(etcButton, selected == InventoryTabType.Etc);
        }

        private static void AddListener(Button button, InventoryTabType tab, Action<InventoryTabType> onTabClicked)
        {
            if (button != null)
            {
                button.onClick.AddListener(() => onTabClicked(tab));
            }
        }

        private static void RemoveListeners(Button button)
        {
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
            }
        }

        private static void SetButtonColor(Button button, bool active)
        {
            if (button != null && button.targetGraphic is Image image)
            {
                image.color = active ? ActiveColor : InactiveColor;
            }
        }
    }
}
