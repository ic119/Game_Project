using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    // 인벤토리/장비/아이템 사용·드롭과 드롭 보상 표시, 스탯 패널 갱신.
    public partial class GameSceneManager
    {
        #region Method
        /// <summary>
        /// 내가 몬스터를 처치해 GameServer가 굴린 골드/아이템 드롭(Game_LootBroadcast, 처치자 본인에게만 옴)을 화면에 반영한다.
        /// HandleExpGained와 마찬가지로 DB 저장은 GameServer가 직접 하므로 여기서는 표시만 한다.
        /// </summary>
        private void HandleLootReceived(GameLootBroadcastPacket packet)
        {
            if (spawnedPlayerModel == null)
            {
                return;
            }

            if (packet.GoldGained > 0)
            {
                spawnedPlayerModel.ApplyGoldGain(packet.GoldGained);
            }

            foreach (GameLootItemEntry item in packet.Items)
            {
                localInventory.AddOrMerge(item.ItemId, item.Qty);
            }

            RefreshInventoryDisplay();

            ShowDropItemPopup(packet.GoldGained, packet.Items);
        }


        /// <summary>
        /// HandleLootReceived가 반영한 획득 골드/드롭 아이템 목록을 UI_DropItemPopupView로 보여준다.
        /// 아이템 없이 골드만 떨어진 경우에도 골드 줄만으로 팝업을 열어, 처치 보상은 항상 같은 팝업을 거치게 한다.
        /// RefreshInventoryDisplay와 동일하게 ItemDatabaseManager.FindById를 조회 함수로 넘긴다
        /// (아직 아이콘이 등록되지 않은 아이템은 UI_DropListItemView가 이름/수량만으로 최소 표시한다).
        /// </summary>
        private void ShowDropItemPopup(int goldGained, List<GameLootItemEntry> items)
        {
            int itemCount = items != null ? items.Count : 0;
            if (dropItemPopupView == null || (goldGained <= 0 && itemCount == 0))
            {
                return;
            }

            Func<string, ItemData> itemLookup = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById : null;
            dropItemPopupView.Show(goldGained, items, itemLookup);
        }


        /// <summary>
        /// ItemDatabaseManager.OnDatabaseLoaded 콜백. 인벤토리가 ItemDatabaseSO 로드 완료 전에 먼저 열려
        /// 아이콘/이름 없이(itemId 텍스트만으로) 표시됐을 수 있으므로, 로드가 끝나는 즉시 한 번 더 갱신해
        /// 뒤늦게라도 정상 아이콘/이름으로 바뀌게 한다.
        /// </summary>
        private void HandleItemDatabaseLoaded()
        {
            // ItemDatabaseSO가 이제야 로드됐다면, 로드 전이라 건너뛰었던 장착 시각 반영/스탯 보너스 계산도 함께 재시도한다.
            ApplyEquippedVisuals();
            RecalculateEquipmentStats();
            RefreshInventoryDisplay();
        }

        /// <summary>
        /// 인벤토리 UI가 이미 생성되어 있으면 골드/아이템 슬롯과 스탯 패널을 현재 런타임 상태(spawnedPlayerModel)로
        /// 다시 그린다. 아이콘/등급은 ItemDatabaseManager를 통해 조회하며, 아직 로드되지 않았거나(부트스트랩 직후)
        /// 디자이너가 해당 itemId를 등록하기 전이면 UI_InventoryView가 알아서 아이콘 없이 최소 정보로 표시한다.
        /// 스탯 패널(공격력/방어력/최대체력)은 CombatStatComponent/HealthComponent가 이미 계산해둔 실제 값을
        /// 그대로 받아 표시하므로, 장비 보너스가 반영된 뒤(RecalculateEquipmentStats 이후) 호출해야 최신값이 보인다.
        /// </summary>
        private void RefreshInventoryDisplay()
        {
            if (inventoryView == null || spawnedPlayerModel == null)
            {
                return;
            }

            Func<string, ItemData> itemLookup = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById : null;
            inventoryView.RefreshInventory(spawnedPlayerModel.Gold, localInventory.Items, itemLookup);
            inventoryView.SetPotionCooldown(PotionCooldownRemainingSeconds); // 다시 그려진 선택 슬롯의 사용 버튼에 남은 대기시간을 반영한다.
            inventoryView.UpdateStatsUI(spawnedPlayerModel.Stats, spawnedPlayerModel.AttackPower, spawnedPlayerModel.Defense, spawnedPlayerModel.MaxHp);
        }

        /// <summary>
        /// 인벤토리가 열려 있을 때만 스탯 패널(능력치/공격력/방어력/최대 체력)을 현재 값으로 다시 그린다. 레벨업처럼 인벤토리를
        /// 열어 둔 채 값이 바뀌는 경우를 위한 가벼운 갱신이다(아이템 슬롯까지 다시 그리는 RefreshInventoryDisplay를 부르지 않는다).
        /// 닫혀 있으면 아무것도 하지 않는다 - 비활성 UI는 텍스트를 바꿔도 다시 그려지지 않고, 어차피 여는 시점
        /// (ToggleInventory)에 RefreshInventoryDisplay가 최신 값으로 그린다.
        /// </summary>
        private void RefreshStatsPanel()
        {
            if (!isInventoryActive || inventoryView == null || spawnedPlayerModel == null)
            {
                return;
            }

            inventoryView.UpdateStatsUI(spawnedPlayerModel.Stats, spawnedPlayerModel.AttackPower, spawnedPlayerModel.Defense, spawnedPlayerModel.MaxHp);
        }

        /// <summary>
        /// localInventory 중 장착 중인(equipSlot이 설정된) 스택들의 ItemData.bonusAttackPower/bonusDefense를
        /// 합산해 CombatStatComponent에 반영하고, GameServer 접속 중이면 갱신된 값을 알린다(Game_StatUpdateRequest).
        /// GameServer는 Game_EnterRequest 시점 스냅샷(PlayerInfo.AttackPower/Defense)을 그대로 캐싱해서 전투 판정에
        /// 쓰기 때문에(GameRoom.ApplyMonsterAttackAsync/AttackPlayerAsync), 이 알림이 없으면 인벤토리에는 스탯이
        /// 올랐다고 뜨지만 실제 몬스터 전투 데미지는 그대로인 불일치가 생긴다.
        /// 장착/해제(TryEquipItem/TryUnequipSlot)와 로그인 복원(ApplySelectedCharacterCustomization,
        /// HandleItemDatabaseLoaded) 양쪽에서 호출된다.
        /// </summary>
        private void RecalculateEquipmentStats(bool notifyServer = true)
        {
            if (spawnedPlayerModel == null || ItemDatabaseManager.Instance == null)
            {
                return;
            }

            localInventory.SumEquipmentBonus(ItemDatabaseManager.Instance.FindById, out int totalAttackBonus, out int totalDefenseBonus);
            spawnedPlayerModel.SetEquipmentBonus(totalAttackBonus, totalDefenseBonus);

            if (notifyServer)
            {
                NotifyServerEquipmentChanged();
            }
        }

        /// <summary>
        /// 장비가 바뀌었음을 GameServer에 알린다(Game_StatUpdateRequest). 서버는 이 요청을 받으면 MainServer에서 캐릭터를 다시 조회해
        /// 장착 무기/방어구와 공격력·방어력을 갱신하므로, MainServer에 장착이 저장된 뒤에 보내야 한다 - 저장보다 먼저 보내면 서버가
        /// 이전 장비를 읽어 가고, 재접속하기 전까지 무기 종류(스킬 판정)와 공격력이 이전 값으로 남는다.
        /// </summary>
        private void NotifyServerEquipmentChanged()
        {
            if (spawnedPlayerModel == null)
            {
                return;
            }

            GameServerConnectManager.Instance?.SendStatUpdate(spawnedPlayerModel.AttackPower, spawnedPlayerModel.Defense);
        }

        /// <summary>
        /// localInventory 중 equipSlot이 설정된(장착 중인) 스택을 실제 캐릭터 장비 시각(PlayerCharacterModel.EquipItem)에
        /// 반영한다. 로그인 직후(캐릭터 복원, ApplySelectedCharacterCustomization)와 ItemDatabaseSO 로드 완료 시점
        /// (HandleItemDatabaseLoaded) 양쪽에서 호출된다 - 아이템 데이터베이스가 아직 로드되지 않은 상태에서 먼저
        /// 호출되면 해당 스택은 건너뛰고, 로드가 끝난 뒤 재호출로 뒤늦게 반영된다. EquipmentController.Equip은
        /// 멱등이라(같은 슬롯에 같은 비주얼을 다시 활성화) 두 번 호출돼도 안전하다.
        /// </summary>
        private void ApplyEquippedVisuals()
        {
            if (spawnedPlayerModel == null || ItemDatabaseManager.Instance == null)
            {
                return;
            }

            foreach (InventoryItemStack stack in localInventory.EquippedStacks())
            {
                ItemData itemData = ItemDatabaseManager.Instance.FindById(stack.itemId);
                if (itemData != null)
                {
                    spawnedPlayerModel.EquipItem(itemData);
                }
            }
        }

        /// <summary>
        /// 인벤토리 슬롯의 "장착/사용" 또는 "장착 해제" 버튼 클릭(UI_InventoryView.OnUseItemRequested)을 처리한다.
        /// 클릭된 슬롯이 일반 인벤토리 칸이면 장비 아이템만 장착 처리하고(소비 아이템 사용은 아직 미구현),
        /// 장비 슬롯이면 장착을 해제한다.
        /// </summary>
private void HandleInventoryUseRequested(UI_InventorySlot _slot)
        {
            if (_slot == null || !_slot.HasItem || spawnedPlayerModel == null)
            {
                return;
            }

            if (_slot.SlotType != InventorySlotType.Inventory)
            {
                TryUnequipSlot(ToEquipmentSlotType(_slot.SlotType));
                return;
            }

            ItemData itemData = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById(_slot.ItemId) : null;
            if (itemData != null && itemData.itemType == ItemType.Potion)
            {
                TryUseHealthPotion(_slot.ItemId, itemData);
                return;
            }

            TryEquipItem(_slot.ItemId);
        }

        /// <summary>
        /// itemId를 장착한다. 장비 아이템이 아니면 조용히 무시한다(물약 사용은 TryUseHealthPotion이 별도로 처리하고, 그 외 소비 아이템 사용은 아직 미구현이다).
        /// 로컬 상태를 먼저 낙관적으로 갱신해 UI/캐릭터 시각을 즉시 반영하고, 서버 저장은 백그라운드로 요청한다
        /// (HandleLootReceived 등 기존 인벤토리 갱신 흐름과 동일한 낙관적 갱신 패턴).
        /// </summary>
private void TryEquipItem(string _itemId)
        {
            ItemData itemData = ItemDatabaseManager.Instance != null ? ItemDatabaseManager.Instance.FindById(_itemId) : null;
            if (itemData == null || itemData.itemType != ItemType.Eqiupment || itemData.equipSlotType == EquipmentSlotType.None)
            {
                return;
            }

            string slotKey = itemData.equipSlotType.ToString();
            if (!localInventory.TryEquip(_itemId, slotKey, out InventoryItemStack targetStack, out InventoryItemStack previouslyEquipped))
            {
                return;
            }

            spawnedPlayerModel.EquipItem(itemData);

            // 서버 통보는 저장 성공 콜백에서 한다(NotifyServerEquipmentChanged 주석 참고). 로컬 스탯은 낙관적으로 바로 반영한다.
            RecalculateEquipmentStats(notifyServer: false);
            RefreshInventoryDisplay();

            SaveDataManager.Instance?.EquipItem(_itemId, itemData.equipSlotType, success =>
            {
                if (success)
                {
                    NotifyServerEquipmentChanged();
                    return;
                }

                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"장비 장착 저장 실패 : {_itemId}");

                // 서버 저장이 실패했으니 로컬 상태를 장착 시도 이전으로 되돌린다(낙관적 갱신 롤백).
                localInventory.RevertEquip(targetStack, previouslyEquipped, slotKey);

                if (previouslyEquipped != null)
                {
                    ItemData previousItemData = ItemDatabaseManager.Instance?.FindById(previouslyEquipped.itemId);
                    if (previousItemData != null)
                    {
                        spawnedPlayerModel.EquipItem(previousItemData);
                    }
                }
                else
                {
                    spawnedPlayerModel.UnequipItem(itemData.equipSlotType);
                }

                // 서버에는 장착 변경을 알린 적이 없으므로(저장 실패) 롤백도 로컬만 반영한다.
                RecalculateEquipmentStats(notifyServer: false);
                RefreshInventoryDisplay();
                GameManager.Instance?.ShowAlarmPopup("장비 장착 실패", "서버 저장에 실패해 장착을 되돌렸습니다.");
            });
        }

/// <summary>
        /// 물약(ItemType.Potion) 사용을 GameServer에 요청한다(Game_UseItemRequest). 체력은 서버가 회복시키고,
        /// 아이템도 서버가 MainServer에서 차감한다 - 클라이언트는 결과(HandleUseItemResult)를 받은 뒤에만 인벤토리
        /// 수량을 줄이고, 회복된 체력은 Game_PlayerHpBroadcast(HandlePlayerHpChanged)로 반영된다.
        /// 사망/만피처럼 서버가 어차피 거절할 요청은 여기서 미리 거른다(효과 없는 사용으로 아이템이 사라지지 않는 건 서버가 보장).
        /// 응답을 받기 전에는 다음 사용을 막아, 연타로 같은 아이템이 여러 번 차감 요청되지 않게 한다.
        /// </summary>
        private void TryUseHealthPotion(string _itemId, ItemData _itemData)
        {
            if (potionState.IsPending || spawnedPlayerModel == null)
            {
                return;
            }

            InventoryItemStack targetStack = localInventory.FindUnequipped(_itemId);
            if (targetStack == null || targetStack.count <= 0)
            {
                return;
            }

            if (_itemData.healPercent <= 0 || spawnedPlayerModel.IsDead || spawnedPlayerModel.CurrentHp >= spawnedPlayerModel.MaxHp)
            {
                return;
            }

            // 재사용 대기시간 중이면 서버가 어차피 거부하므로 요청을 보내지 않는다(서버 판정이 최종이다).
            if (PotionCooldownRemainingSeconds > 0f)
            {
                return;
            }

            potionState.IsPending = GameServerConnectManager.Instance != null && GameServerConnectManager.Instance.SendUseItem(_itemId);
        }

        /// <summary>
        /// Game_UseItemResult(내 요청에 대한 결과). 서버에서 실제로 차감됐을 때만 로컬 인벤토리 수량을 1 줄인다.
        /// </summary>
private void HandleUseItemResult(GameUseItemResultPacket packet)
        {
            potionState.IsPending = false;

            // 성공/실패와 무관하게 서버가 알려준 남은 대기시간으로 갱신한다(성공하면 방금 시작된 대기시간, Cooldown 거부면 남은 시간).
            potionState.SetCooldownMs(packet.CooldownRemainingMs);
            inventoryView?.SetPotionCooldown(PotionCooldownRemainingSeconds);

            if (!packet.Success)
            {
                return;
            }

            PlayHpPotionEffect();

            if (!localInventory.ConsumeOne(packet.ItemId))
            {
                return;
            }

            RefreshInventoryDisplay();
        }

/// <summary>
        /// 인벤토리 슬롯의 "버리기" 버튼 클릭(UI_InventoryView.OnDropItemRequested)을 처리한다. 장착 중인 장비
        /// 슬롯(SlotType != Inventory)은 버릴 수 없다 - 먼저 장착 해제해 일반 인벤토리 칸으로 옮긴 뒤에만 버릴 수 있다.
        /// </summary>
        private void HandleInventoryDropRequested(UI_InventorySlot _slot)
        {
            if (_slot == null || !_slot.HasItem || _slot.SlotType != InventorySlotType.Inventory)
            {
                return;
            }

            TryDropItem(_slot.ItemId);
        }

        /// <summary>
        /// itemId에 해당하는 미장착 스택 전체를 인벤토리에서 제거한다(개별 수량이 아니라 슬롯 단위로 통째로 버린다).
        /// TryEquipItem/TryUseHealthPotion과 동일한 낙관적 로컬 갱신 패턴을 따른다 - 로컬 상태를 먼저 갱신해
        /// UI에 즉시 반영하고, 서버 저장은 백그라운드로 요청한다(DELETE api/characters/{id}/items/{itemId}).
        /// </summary>
private void TryDropItem(string _itemId)
        {
            InventoryItemStack targetStack = localInventory.RemoveUnequipped(_itemId);
            if (targetStack == null)
            {
                return;
            }

            RefreshInventoryDisplay();

            SaveDataManager.Instance?.RemoveItem(_itemId, success =>
            {
                if (success)
                {
                    return;
                }

                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"아이템 버리기 저장 실패 : {_itemId}");

                // 서버 저장이 실패했으니 로컬 상태를 버리기 이전으로 되돌린다(낙관적 갱신 롤백).
                localInventory.Restore(targetStack);
                RefreshInventoryDisplay();
                GameManager.Instance?.ShowAlarmPopup("아이템 버리기 실패", "서버 저장에 실패해 아이템을 되돌렸습니다.");
            });
        }



        /// <summary>
        /// 지정한 장비 슬롯을 해제한다. TryEquipItem과 대칭되는 낙관적 갱신 흐름을 따른다.
        /// </summary>
private void TryUnequipSlot(EquipmentSlotType _slotType)
        {
            if (_slotType == EquipmentSlotType.None)
            {
                return;
            }

            string slotKey = _slotType.ToString();
            if (!localInventory.TryUnequip(slotKey, out InventoryItemStack equippedStack))
            {
                return;
            }

            spawnedPlayerModel.UnequipItem(_slotType);
            RecalculateEquipmentStats(notifyServer: false);
            RefreshInventoryDisplay();

            SaveDataManager.Instance?.UnequipItem(_slotType, success =>
            {
                if (success)
                {
                    NotifyServerEquipmentChanged();
                    return;
                }

                DebugLogManager.GenerateErrorMessage<GameSceneManager>($"장비 해제 저장 실패 : {_slotType}");

                // 서버 저장이 실패했으니 로컬 상태를 해제 시도 이전으로 되돌린다(낙관적 갱신 롤백).
                localInventory.RevertUnequip(equippedStack, slotKey);

                ItemData itemData = ItemDatabaseManager.Instance?.FindById(equippedStack.itemId);
                if (itemData != null)
                {
                    spawnedPlayerModel.EquipItem(itemData);
                }

                RecalculateEquipmentStats(notifyServer: false);
                RefreshInventoryDisplay();
                GameManager.Instance?.ShowAlarmPopup("장비 해제 실패", "서버 저장에 실패해 해제를 되돌렸습니다.");
            });
        }

        /// <summary>
        /// UI_InventorySlot.InventorySlotType(뷰 계층의 슬롯 종류)을 EquipmentSlotType(모델 계층의 장비 슬롯 종류)으로
        /// 변환한다. 일반 인벤토리 칸(Inventory)이면 장비 슬롯이 아니므로 None을 반환한다.
        /// </summary>
        private static EquipmentSlotType ToEquipmentSlotType(InventorySlotType _slotType) => _slotType switch
        {
            InventorySlotType.EquipmentWeapon => EquipmentSlotType.Weapon,
            InventorySlotType.EquipmentArmor => EquipmentSlotType.Armor,
            InventorySlotType.EquipmentHelmet => EquipmentSlotType.Helmet,
            InventorySlotType.EquipmentAccessory => EquipmentSlotType.Accessory,
            _ => EquipmentSlotType.None
        };

        /// <summary>
        /// I키 입력 시 호출된다. isInventoryActive를 기준으로 판단해 꺼져 있으면 켜고, 켜져 있으면 끈다.
        /// 인벤토리가 아직 생성되지 않았다면(SpawnInventoryUI 완료 전) 아무 것도 하지 않는다.
        /// </summary>
        private void ToggleInventory()
        {
            if (inventoryInstance == null)
            {
                return;
            }

            if (isInventoryActive)
            {
                inventoryInstance.SetActive(false);
                isInventoryActive = false;
            }
            else
            {
                // 반드시 SetActive(true)를 먼저 하고 그 다음에 갱신해야 한다. TextMeshPro/Image 등 UI Graphic은
                // 비활성 상태에서 텍스트/스프라이트를 바꿔도 내부적으로 SetVerticesDirty 등이 "IsActive()==false면
                // 무시"하기 때문에 다시 그려지도록 예약되지 않는다 - 그래서 활성화 전에 RefreshInventoryDisplay를
                // 먼저 호출하면(과거 코드) 처음 열 때는 골드/아이템이 비어 보이고, 한 번 껐다 켜야만(그 사이
                // 다른 경로로 한 번 더 갱신되며) 반영되는 문제가 있었다. 활성화부터 한 뒤에 갱신하면 첫 번째
                // 여는 시점부터 항상 정상적으로 보인다.
                inventoryInstance.SetActive(true);
                isInventoryActive = true;
                RefreshInventoryDisplay();
            }
        }

        #endregion
    }
}
