using UnityEngine;

namespace JJORY.Utils
{
    public class SingletonObject<T> : MonoBehaviour where T : MonoBehaviour
    {
        #region Variable
        private static T instance;
        public static bool isInitialized = false; // 중복 생성 방지 용도의 플래그변수
        
        public static T Instance
        {
            get
            {
                if (instance == null && isInitialized == false)
                {
                    instance = FindFirstObjectByType<T>();
                    isInitialized = true;
                    
                    if (instance == null)
                    {
                        Debug.Log($">>>>> {typeof(T).Name} 인스턴스를 찾을 수 없습니다.");
                    }
                }

                return instance;
            }
        }
        #endregion

        #region LifeCycle
        private void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
                isInitialized = true;
            }
            else if (instance != this)
            {
                Debug.Log($">>>>> {typeof(T).Name} 인스턴스가 이미 존재하고 있습니다.");
                Destroy(gameObject);
            }
        }
        #endregion
    }
}