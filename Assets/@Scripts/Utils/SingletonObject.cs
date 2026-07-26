using UnityEngine;

namespace Incheol.Util
{
    public class SingletonObject<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = GameObject.FindAnyObjectByType<T>();
                    if (instance == null)
                    {
                        GameObject goInstance = new GameObject();
                        instance = goInstance.AddComponent<T>();
                        instance.name = typeof(T).Name;

                        // 자동 생성된 싱글톤은 씬 전환(Single 로드/언로드) 시 파괴되지 않도록 유지한다.
                        Object.DontDestroyOnLoad(goInstance);
                    }
                }
                return instance;
            }
        }
    }
}