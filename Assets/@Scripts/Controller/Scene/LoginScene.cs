using JJORY.Module;
using UnityEngine;


namespace JJORY.Scene
{
    public class LoginScene : MonoBehaviour
    {
        #region Variable
        #endregion

        #region LifeCycle
        private void Awake()
        {
            AddressableController.Instance.LoadPrefab<GameObject>("UI_LoginScene");
        }

        private void Start()
        {
            //GameObject go = AddressableController.Instance.InstantiatePrefab<GameObject>("UI_LoginScene");
        }
        #endregion

        #region Method
        #endregion
    }
}