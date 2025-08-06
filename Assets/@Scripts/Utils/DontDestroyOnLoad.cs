using UnityEngine;

namespace JJORY.Utils
{
    public class DontDestroyOnLoad : MonoBehaviour
    {
        #region LifeCycle
        private void Start()
        {
            DontDestroyOnLoad(this.gameObject);
        }
        #endregion
    }
}