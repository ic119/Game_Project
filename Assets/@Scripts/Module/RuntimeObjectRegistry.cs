using System.Collections.Generic;
using Incheol.Util;
using UnityEngine;

namespace Incheol.Module
{
    /// <summary>
    /// Addressable 등으로 생성한 런타임 오브젝트를 키로 등록/조회한다.
    /// FindInChildren / GameObject.Find 대신 생성 시점 등록을 사용한다.
    /// </summary>
    public class RuntimeObjectRegistry : SingletonObject<RuntimeObjectRegistry>
    {
        #region Variable
        private readonly Dictionary<string, GameObject> registeredObjects = new Dictionary<string, GameObject>();
        #endregion

        #region Method
        public void Register(string _key, GameObject _object)
        {
            if (string.IsNullOrEmpty(_key) || _object == null)
            {
                return;
            }

            registeredObjects[_key] = _object;
        }

        public bool TryGet(string _key, out GameObject _object)
        {
            if (registeredObjects.TryGetValue(_key, out _object) && _object != null)
            {
                return true;
            }

            _object = null;
            registeredObjects.Remove(_key);
            return false;
        }

        public GameObject Get(string _key)
        {
            return TryGet(_key, out GameObject go) ? go : null;
        }

        public void Unregister(string _key)
        {
            if (string.IsNullOrEmpty(_key))
            {
                return;
            }

            registeredObjects.Remove(_key);
        }

        public void Clear()
        {
            registeredObjects.Clear();
        }
        #endregion
    }
}
