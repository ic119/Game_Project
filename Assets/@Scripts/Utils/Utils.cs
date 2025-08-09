using UnityEngine;

namespace JJORY.Util
{
    public class Utils
    {
        public static void CreateLogMessage<T>(string _message)
        {
            Debug.Log($">>>>> {typeof(T).Name} : {_message}");
        }

        public static void CreateLogError<T>(string _message)
        {
            Debug.LogError($">>>>> {typeof(T).Name} : {_message}");
        }
    }
}


