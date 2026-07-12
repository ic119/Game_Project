using JJORY.Module;
using UnityEngine;

public class UI_InventoryViewPopupController : MonoBehaviour
{
    private void Awake()
    {
        RuntimeObjectRegistry.Instance.Register(AddressKey.UI_InventoryViewPopup.ToString(), gameObject);
    }

    private void OnDestroy()
    {
        if (RuntimeObjectRegistry.Instance != null)
        {
            RuntimeObjectRegistry.Instance.Unregister(AddressKey.UI_InventoryViewPopup.ToString());
        }
    }
}
