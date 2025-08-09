using UnityEngine;

namespace JJORY.Util
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