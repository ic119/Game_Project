using UnityEngine;

namespace Incheol.Utils
{
    /// <summary>
    /// ItemGrade에 대응하는 표시 색상/한글 등급명을 제공한다.
    /// UI_InventorySlot의 GradeBorder, UI_GameSceneView의 몬스터 이름표 등 등급을 표현하는
    /// 모든 곳에서 이 값을 공용으로 사용한다.
    /// </summary>
    public static class ItemGradeUtils
    {
        private static readonly Color CommonColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color RareColor = new Color32(0x3B, 0x82, 0xF6, 0xFF);
        private static readonly Color EpicColor = new Color32(0xA8, 0x55, 0xF7, 0xFF);
        private static readonly Color LegendaryColor = new Color32(0xF5, 0x9E, 0x0B, 0xFF);

        public static Color GetGradeColor(this ItemGrade _grade)
        {
            switch (_grade)
            {
                case ItemGrade.Rare:
                    return RareColor;
                case ItemGrade.Epic:
                    return EpicColor;
                case ItemGrade.Legendary:
                    return LegendaryColor;
                case ItemGrade.Common:
                default:
                    return CommonColor;
            }
        }

        public static string GetGradeDisplayName(this ItemGrade _grade)
        {
            switch (_grade)
            {
                case ItemGrade.Rare:
                    return "희귀";
                case ItemGrade.Epic:
                    return "영웅";
                case ItemGrade.Legendary:
                    return "전설";
                case ItemGrade.Common:
                default:
                    return "일반";
            }
        }
    }
}
