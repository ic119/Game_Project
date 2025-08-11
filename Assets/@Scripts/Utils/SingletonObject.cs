using UnityEngine;

namespace JJORY.Util
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
                    }
                }
                return instance;
            }
        }
    }
}