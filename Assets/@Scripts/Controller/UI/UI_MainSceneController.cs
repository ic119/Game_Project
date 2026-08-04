using Incheol.Module;
using UnityEngine;

public class UI_MainSceneController : MonoBehaviour
{
    #region Variable
    [Header("View 변수")]
    [SerializeField] private UI_MainSceneView mainSceneView;

    private bool isMenuButtonClicked;
    private bool isLoggingOut;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        if (mainSceneView == null)
        {
            mainSceneView = GetComponent<UI_MainSceneView>();
        }

        isMenuButtonClicked = mainSceneView.IsMainPopupActive;
    }

    private void OnEnable()
    {
        mainSceneView.MenuButtonClicked += OnClickMenuButton;
        mainSceneView.LogoutButtonClicked += OnClickLogoutButton;
    }

    private void OnDisable()
    {
        mainSceneView.MenuButtonClicked -= OnClickMenuButton;
        mainSceneView.LogoutButtonClicked -= OnClickLogoutButton;
    }
    #endregion

    #region Method
    private void OnClickMenuButton()
    {
        isMenuButtonClicked = !isMenuButtonClicked;
        mainSceneView.SetMainPopupActive(isMenuButtonClicked);
    }

    private void OnClickLogoutButton()
    {
        if (isLoggingOut)
        {
            return;
        }

        isLoggingOut = true;
        Destroy(gameObject);
        LoadSceneController.Instance.LoadSceneByTags(AddressKey.UI_LoginScene);
    }
    #endregion
}
