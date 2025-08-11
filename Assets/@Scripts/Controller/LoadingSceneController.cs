using JJORY.Module;
using TMPro;
using UnityEngine;

public class LoadingSceneController : MonoBehaviour
{
    #region Variable
    [SerializeField] TextMeshProUGUI loadingText;
    #endregion

    #region LifeCycle
    #endregion

    void Update()
    {
        float progress = SceneLoadController.Instance.cur_LoadProgress;
        progress *= 100.0f;
        int nProgress = (int)progress;
        loadingText.text = $"{nProgress}%";
    }
}