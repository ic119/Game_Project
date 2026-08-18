using UnityEngine;

namespace Incheol.Utils
{
    public class ModuleDestroyController : MonoBehaviour
    {
        #region LifeCycle
        private void Start()
        {
            DontDestroyOnLoad(gameObject);
        }
        #endregion
    }
}