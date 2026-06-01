using JJORY.Model.Player;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

namespace JJORY.Controller.UI
{
    public class BillBoardController : MonoBehaviour
    {
        #region Variable
        [SerializeField] private Transform mainCameraTr;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private GameObject nameLabel;
        [SerializeField] private TextMeshProUGUI userNameLabelTMP;
        [SerializeField] private PlayerModel playerModel;
        #endregion

        #region LifeCycle
        private void Start()
        {
            if (playerModel == null)
            {
                playerModel = GetComponent<PlayerModel>();
            }

            if (userNameLabelTMP != null)
            {
                userNameLabelTMP.text = $"{playerModel.playerGameInfo.UserName}";
            }
        }

        private void LateUpdate()
        {
            if (mainCamera == null)
            {
                if (Camera.main == null)
                {
                    return;
                }

                mainCamera = Camera.main;
                mainCameraTr = mainCamera.transform;
            }

            nameLabel.transform.rotation = mainCameraTr.rotation;
        }
        #endregion
    }
}
