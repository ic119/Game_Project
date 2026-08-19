using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Incheol.View.UI
{
    public class UI_RegistPopupView : MonoBehaviour
    {
        #region Variable
        [SerializeField] private Button createButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private UI_LoginSceneView loginSceneView;
        [SerializeField] private TMP_InputField accountInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private TMP_InputField passwordCheckInputField;
        #endregion

        #region LifeCycle
        private void Awake()
        {
            createButton.onClick.AddListener(OnClickCreateButton);
            cancelButton.onClick.AddListener(OnClickCancelButton);
            passwordInputField.onValueChanged.AddListener(OnPasswordValueChanged);
            passwordCheckInputField.onValueChanged.AddListener(OnPasswordValueChanged);

            createButton.interactable = false;
        }
        #endregion

        #region Method
        private void OnClickCreateButton()
        {
            accountInputField.text = null;
            passwordInputField.text = null;
            passwordCheckInputField.text = null;
            createButton.interactable = false;

            gameObject.SetActive(false);

            if (loginSceneView != null)
            {
                loginSceneView.SetRegistPopupActive(false);
            }
        }

        private void OnClickCancelButton()
        {
            accountInputField.text = null;
            passwordInputField.text = null;
            passwordCheckInputField.text = null;
            createButton.interactable = false;

            gameObject.SetActive(false);

            if (loginSceneView != null)
            {
                loginSceneView.SetRegistPopupActive(false);
            }
        }

        private void OnPasswordValueChanged(string _)
        {
            createButton.interactable = passwordInputField.text == passwordCheckInputField.text;
        }

        #endregion
    }
}
