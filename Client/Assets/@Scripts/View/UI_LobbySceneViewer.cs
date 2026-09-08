using UnityEngine;
using UnityEngine.UI;
using Incheol.Modules;

public class UI_LobbySceneViewer : MonoBehaviour
{
    #region Variable
    [SerializeField] private GameObject maskImage;

    [Header("Lobby Scene Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button exitButton;

    [Header("Popups")]
    [SerializeField] private UI_CharacterCreatePopup characterCreatePopup;

    public UI_CharacterCreatePopup CharacterCreatePopup => characterCreatePopup;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        if (maskImage != null)
        {
            maskImage.SetActive(false);
        }

        if (continueButton != null)
        {
            bool hasSaveData = GameManager.Instance != null && GameManager.Instance.HasSaveData;
            continueButton.gameObject.SetActive(hasSaveData);
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnClickStartButton);
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(OnClickExitButton);
        }

        if (continueButton != null)
        {
            continueButton.onClick.AddListener(OnClickContinueButton);
        }
    }

    private void OnDestroy()
    {
        if (startButton != null) startButton.onClick.RemoveAllListeners();
        if (continueButton != null) continueButton.onClick.RemoveAllListeners();
        if (exitButton != null) exitButton.onClick.RemoveAllListeners();
    }
    #endregion

    #region Method
    private void OnClickStartButton()
    {
        if (characterCreatePopup != null)
        {
            characterCreatePopup.Open();
        }
    }

    private void OnClickContinueButton()
    {
        // 이어하기 로직
        if (SceneLoadManager.Instance != null)
        {
            SceneLoadManager.Instance.LoadSceneByTags("GameScene");
        }
    }

    private void OnClickExitButton()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    #endregion
}
