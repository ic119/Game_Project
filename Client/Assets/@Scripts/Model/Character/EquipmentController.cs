using System.Collections.Generic;
using Incheol.Utils;
using UnityEngine;

/// <summary>
/// 장비 컨테이너(rightArmEqiupment 등) 하위에 이미 배치된 프리팹 변형(메시)들 중 하나만 활성화하는 방식으로
/// 장비를 교체한다. CharacterCustomModel(헤어/눈/입)과 같은 SetActive 토글 메커니즘을 쓰되, 리스트+인덱스를
/// Inspector에 일일이 등록/유지하는 대신 컨테이너의 자식을 이름으로 자동 인덱싱한다 - 프리팹에 새 메시 변형이
/// 추가/삭제되어도 이 스크립트나 Inspector 리스트를 건드릴 필요 없이 그대로 장착 대상이 된다.
/// itemId ↔ 표시할 메시 오브젝트 이름의 연결은 ItemData.equipVisualName이 담당한다.
/// </summary>
public class EquipmentController : MonoBehaviour
{
    [Header("장비 컨테이너 (하위 자식 오브젝트를 이름으로 자동 인덱싱)")]
    [SerializeField] private GameObject bodyEquipment;
    [SerializeField] private GameObject backPackEquipment;
    [SerializeField] private GameObject cloakEquipment;
    [SerializeField] private GameObject leftArmEquipment;
    [SerializeField] private GameObject rightArmEquipment;

    /// <summary>
    /// 슬롯별로 탐색할 컨테이너 우선순위. Weapon은 한손/두손/완드/창(오른팔)과 방패/활(왼팔)을 모두 포함하므로
    /// 오른팔을 먼저 찾고 없으면 왼팔에서 찾는다. Helmet/Boots/Accessory는 아직 대응하는 컨테이너가 없다 - 알려진 한계.
    /// </summary>
    private Dictionary<EquipmentSlotType, GameObject[]> containersBySlot;

    /// <summary>
    /// 컨테이너별 "자식 오브젝트 이름 -> GameObject" 인덱스. 오른팔/왼팔 컨테이너에 같은 이름(OHS01 등)이
    /// 중복으로 존재할 수 있어(양손 중 어느 쪽에도 낄 수 있는 무기 등) 하나의 딕셔너리로 합치지 않고
    /// 컨테이너 단위로 분리해 관리한다.
    /// </summary>
    private readonly Dictionary<GameObject, Dictionary<string, GameObject>> visualsByContainer = new Dictionary<GameObject, Dictionary<string, GameObject>>();

    private readonly Dictionary<EquipmentSlotType, GameObject> activeVisualBySlot = new Dictionary<EquipmentSlotType, GameObject>();

    private bool isIndexed;

    /// <summary>
    /// 인덱싱을 Awake가 아니라 최초 Equip/Unequip 호출 시점에 지연 수행한다. 같은 GameObject의 Awake 호출 순서는
    /// Unity가 보장하지 않으므로, PlayerCharacterModel.Awake()가 EquipmentController.Awake()보다 먼저 실행되어
    /// 인덱스가 비어있는 채로 초기 장착(EquipWeapon)이 조용히 실패하는 상황을 막기 위함이다.
    /// </summary>
    private void EnsureIndexed()
    {
        if (isIndexed)
        {
            return;
        }

        containersBySlot = new Dictionary<EquipmentSlotType, GameObject[]>
        {
            { EquipmentSlotType.Weapon, new[] { rightArmEquipment, leftArmEquipment } },
            { EquipmentSlotType.Armor, new[] { bodyEquipment } },
        };

        IndexContainer(bodyEquipment);
        IndexContainer(backPackEquipment);
        IndexContainer(cloakEquipment);
        IndexContainer(leftArmEquipment);
        IndexContainer(rightArmEquipment);

        isIndexed = true;
    }

    private void IndexContainer(GameObject container)
    {
        if (container == null)
        {
            return;
        }

        var visuals = new Dictionary<string, GameObject>();
        Transform containerTransform = container.transform;
        for (int i = 0; i < containerTransform.childCount; i++)
        {
            Transform child = containerTransform.GetChild(i);
            visuals[child.name] = child.gameObject;
        }

        visualsByContainer[container] = visuals;
    }

    /// <summary>
    /// slot에 대응하는 컨테이너들에서 이름이 visualName과 일치하는 자식 오브젝트를 찾아 활성화하고,
    /// 같은 컨테이너 안의 나머지 형제 오브젝트는 전부 비활성화한다(CharacterCustomModel.SetHair 등과 동일하게,
    /// EquipmentController.Equip을 거치지 않고 프리팹 원본에서부터 활성 상태였던 오브젝트 - 예: Body 슬롯의
    /// 기본 활성 변형 - 까지 확실히 꺼야 두 변형이 동시에 보이는 문제를 막을 수 있다).
    /// 대응하는 컨테이너가 없거나(Helmet/Boots/Accessory 등 아직 미지원 슬롯) visualName을 찾지 못하면 false를 반환한다.
    /// </summary>
    public bool Equip(EquipmentSlotType slot, string visualName)
    {
        if (string.IsNullOrEmpty(visualName))
        {
            return false;
        }

        EnsureIndexed();

        if (!containersBySlot.TryGetValue(slot, out GameObject[] containers))
        {
            DebugLogManager.GenerateErrorMessage<EquipmentController>($"슬롯 '{slot}'에 대응하는 장비 컨테이너가 아직 없습니다.");
            return false;
        }

        foreach (GameObject container in containers)
        {
            if (container == null || !visualsByContainer.TryGetValue(container, out Dictionary<string, GameObject> visuals))
            {
                continue;
            }

            if (!visuals.TryGetValue(visualName, out GameObject target))
            {
                continue;
            }

            foreach (GameObject visual in visuals.Values)
            {
                visual.SetActive(visual == target);
            }

            activeVisualBySlot[slot] = target;
            return true;
        }

        DebugLogManager.GenerateErrorMessage<EquipmentController>($"슬롯 '{slot}'에서 '{visualName}' 이름의 메시를 찾지 못했습니다.");
        return false;
    }

    /// <summary>
    /// slot에 현재 활성화된 장비 시각 오브젝트가 있다면 비활성화만 한다(빈 손/맨몸 상태로 되돌림).
    /// </summary>
    public void Unequip(EquipmentSlotType slot)
    {
        EnsureIndexed();

        if (activeVisualBySlot.TryGetValue(slot, out GameObject current) && current != null)
        {
            current.SetActive(false);
        }

        activeVisualBySlot.Remove(slot);
    }
}
