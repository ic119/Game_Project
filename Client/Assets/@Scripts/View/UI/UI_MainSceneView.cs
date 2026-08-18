using System;
using UnityEngine;
using UnityEngine.UI;

public class UI_MainSceneView : MonoBehaviour
{
    #region Variable
    [Header("UI 변수")]
    [SerializeField] private GameObject mainPopup;
    [SerializeField] private GameObject blockedImage;
    [SerializeField] private Button optionButton;
    [SerializeField] private Button menuButton;
    [SerializeField] private Button logoutButton;
    #endregion

    #region LifeCycle
    private void OnEnable()
    {
        menuButton.onClick.AddListener(OnClickMenuButton);
        logoutButton.onClick.AddListener(OnClickLogoutButton);
    }

    private void OnDisable()
    {
        menuButton.onClick.RemoveListener(OnClickMenuButton);
        logoutButton.onClick.RemoveListener(OnClickLogoutButton);
    }
    #endregion

    #region Method
    public event Action MenuButtonClicked;
    public event Action LogoutButtonClicked;

    public bool IsMainPopupActive => mainPopup != null && mainPopup.activeSelf;

    public void SetMainPopupActive(bool _isActive)
    {
        if (mainPopup != null)
        {
            mainPopup.SetActive(_isActive);
        }

        if (blockedImage != null)
        {
            blockedImage.SetActive(_isActive);
        }
    }

    private void OnClickMenuButton()
    {
        MenuButtonClicked?.Invoke();
    }

    private void OnClickLogoutButton()
    {
        LogoutButtonClicked?.Invoke();
    }
    #endregion
}
