using System.Collections.Generic;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.View.UI
{
    /// <summary>
    /// 인벤토리 상세정보 패널(UI_InventoryView)에 표시할 텍스트를 만든다. 화면 요소를 전혀 모르는 순수 함수라
    /// UI 없이도 결과 문자열을 확인할 수 있다. TextMeshPro 리치 텍스트(&lt;color&gt;)를 쓴다.
    /// 색상 규칙: 늘어나는 효과(공격력/방어력 증가, 체력 회복)는 연한 파랑, 줄어드는 효과(마이너스 보너스,
    /// 장착 중인 장비보다 나빠지는 변화량)는 빨강, 변화 없음(0)은 회색이다.
    /// </summary>
    public static class ItemDetailTextBuilder
    {
        private const string IncreaseColor = "#66C2FF";
        private const string DecreaseColor = "#FF5A5A";
        private const string NeutralColor = "#9CA3AF";

        /// <summary>
        /// 이름 옆에 등급을 등급 색상으로 붙인다. 예) "기사의 검 [희귀]". 아이템 정보가 없으면(데이터베이스 미로드/미등록) itemId 그대로.
        /// </summary>
        public static string BuildTitle(ItemData _itemData, string _fallbackItemId)
        {
            if (_itemData == null)
            {
                return _fallbackItemId;
            }

            string gradeColor = ColorUtility.ToHtmlStringRGB(_itemData.itemGrade.GetGradeColor());
            return $"{_itemData.itemName} <color=#{gradeColor}>[{_itemData.itemGrade.GetGradeDisplayName()}]</color>";
        }

        /// <summary>
        /// 상세정보 패널의 "종류" 텍스트(예: "장비 · 무기", "물약", "기타"). 장비류는 좌측 장비 슬롯 라벨과 같은 표기
        /// (무기/갑옷/투구/장신구)를 써서 용어가 갈리지 않게 한다.
        /// </summary>
        public static string BuildTypeLabel(ItemData _itemData)
        {
            switch (_itemData.itemType)
            {
                case ItemType.Eqiupment:
                    return $"장비 · {GetEquipmentSlotLabel(_itemData.equipSlotType)}";
                case ItemType.Potion:
                    return "물약";
                default:
                    return "기타";
            }
        }

        private static string GetEquipmentSlotLabel(EquipmentSlotType _slotType)
        {
            switch (_slotType)
            {
                case EquipmentSlotType.Weapon: return "무기";
                case EquipmentSlotType.Armor: return "갑옷";
                case EquipmentSlotType.Helmet: return "투구";
                case EquipmentSlotType.Accessory: return "장신구";
                default: return "장비";
            }
        }

        /// <summary>
        /// 능력치/효과 한 줄(또는 빈 문자열 - 표시할 효과가 없는 기타 아이템).
        /// _equippedInSameSlot은 같은 슬롯에 지금 장착 중인 다른 장비다. 주어지면(일반 칸에서 고른 장비를 비교할 때) 장착 시
        /// 바뀌는 양을 괄호로 덧붙인다. 예) "공격력 +12 (+4)   방어력 +5 (-2)".
        /// _maxHp가 양수면 물약의 실제 회복량도 함께 보여준다.
        /// </summary>
        public static string BuildEffectLine(ItemData _itemData, ItemData _equippedInSameSlot, int _maxHp)
        {
            if (_itemData == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (_itemData.itemType == ItemType.Eqiupment)
            {
                bool compare = _equippedInSameSlot != null && _equippedInSameSlot.itemId != _itemData.itemId;
                AddStat(parts, "공격력", _itemData.bonusAttackPower, compare ? _equippedInSameSlot.bonusAttackPower : (int?)null);
                AddStat(parts, "방어력", _itemData.bonusDefense, compare ? _equippedInSameSlot.bonusDefense : (int?)null);
            }
            else if (_itemData.itemType == ItemType.Potion && _itemData.healPercent > 0)
            {
                string heal = $"체력 {Colored(IncreaseColor, $"{_itemData.healPercent}% 회복")}";
                if (_maxHp > 0)
                {
                    // 서버 CombatStatCalculator.CalculateHealAmount와 같은 공식(최대체력의 healPercent%, 최소 1).
                    int amount = Mathf.Max(1, Mathf.RoundToInt(_maxHp * _itemData.healPercent / 100f));
                    heal += $" {Colored(IncreaseColor, $"(+{amount})")}";
                }

                parts.Add(heal);

                // 재사용 대기시간은 증가/감소 효과가 아닌 정보라 회색으로 표시한다. 0이면(대기시간 없는 물약) 생략.
                if (_itemData.useCooldownSeconds > 0f)
                {
                    parts.Add(Colored(NeutralColor, $"재사용 {FormatSeconds(_itemData.useCooldownSeconds)}초"));
                }
            }

            return string.Join("   ", parts);
        }

        // _current: 이 아이템의 보너스 값. _equipped: 비교 대상(장착 중인 장비)의 값, 비교하지 않으면 null.
        // 이 아이템도 장착 중인 장비도 그 능력치가 0이면 줄 자체를 만들지 않는다(예: 갑옷의 공격력).
        private static void AddStat(List<string> _parts, string _label, int _current, int? _equipped)
        {
            if (_current == 0 && (_equipped == null || _equipped.Value == 0))
            {
                return;
            }

            string text = $"{_label} {Colored(ColorOfSign(_current), FormatSigned(_current))}";

            if (_equipped != null)
            {
                int delta = _current - _equipped.Value;
                if (delta != 0)
                {
                    text += $" {Colored(ColorOfSign(delta), $"({FormatSigned(delta)})")}";
                }
            }

            _parts.Add(text);
        }

        /// <summary>
        /// 초 단위 시간 표기. 정수면 "12", 소수가 있으면 한 자리 "6.5". 재사용 시간 표시와 남은 대기시간 표시가 같은 형식을 쓴다.
        /// </summary>
        public static string FormatSeconds(float _seconds)
        {
            return _seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 대기 중인 사용 버튼의 문구. 남은 시간을 올림한 소수 한 자리로 보여줘 카운트다운이 끊기지 않고 0.0으로 끝나지 않게 한다.
        /// 예) 3.2초 남았으면 "사용 대기 3.2초".
        /// </summary>
        public static string BuildCooldownButtonLabel(float _remainingSeconds)
        {
            float rounded = Mathf.Ceil(_remainingSeconds * 10f) / 10f;
            return $"사용 대기 {rounded.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}초";
        }

        private static string ColorOfSign(int _value)
        {
            if (_value > 0) return IncreaseColor;
            if (_value < 0) return DecreaseColor;
            return NeutralColor;
        }

        private static string FormatSigned(int _value)
        {
            return _value > 0 ? $"+{_value}" : _value.ToString(); // 음수는 ToString이 '-'를 붙이고 0은 그냥 "0".
        }

        private static string Colored(string _hexColor, string _text)
        {
            return $"<color={_hexColor}>{_text}</color>";
        }
    }
}
