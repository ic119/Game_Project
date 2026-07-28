using System.Collections.Generic;
using Incheol.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Module
{
    public class AddressableAssetController : SingletonObject<AddressableAssetController>
    {
        #region Variable
        private readonly Dictionary<AddressKey, AsyncOperationHandle> handle_Dictionary
            = new Dictionary<AddressKey, AsyncOperationHandle>();

        /// <summary>
        /// 중복 Load를 방지하기 위해 이미 Load 요청된 Key를 관리하는 HashSet
        /// </summary>
        private readonly HashSet<AddressKey> key_HashSet = new HashSet<AddressKey>();
        #endregion

        #region LifeCycle

        #endregion

        #region Method
        /// <summary>
        /// AddressKey를 통해 Addressable Asset을 비동기로 Load한다.
        /// HashSet에 등록되지 않은 Key만 신규로 Load하며,
        /// 이미 등록된(중복된) Key는 Load를 시도하지 않고 캐시된 결과를 반환한다.
        /// </summary>
        public async Awaitable<T> LoadAsync<T>(AddressKey _key) where T : UnityEngine.Object
        {
            if (key_HashSet.Contains(_key))
            {
                if (handle_Dictionary.TryGetValue(_key, out AsyncOperationHandle existingHandle)
                    && existingHandle.IsValid()
                    && existingHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    return existingHandle.Result as T;
                }

                return null;
            }

            key_HashSet.Add(_key);

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(_key.ToString());

            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync();
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Utils.CreateLogError<AddressableAssetController>($"LoadAsync 실패: {_key}");

                key_HashSet.Remove(_key);

                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                return null;
            }

            handle_Dictionary[_key] = handle;
            return handle.Result;
        }

        /// <summary>
        /// AddressKey로 Load된 Asset의 Handle을 해제하고, 재Load가 가능하도록 HashSet에서도 제거한다.
        /// </summary>
        public void Release(AddressKey _key)
        {
            if (handle_Dictionary.TryGetValue(_key, out AsyncOperationHandle handle))
            {
                Addressables.Release(handle);
                handle_Dictionary.Remove(_key);
            }

            key_HashSet.Remove(_key);
        }


        /// <summary>
        /// 이미 Load된 AddressKey를 검색하여 Instantiate한다.
        /// _parent가 null이면 최상위(Root)에, null이 아니면 해당 Transform의 자식으로 생성된다.
        /// </summary>
        public GameObject Instantiate(AddressKey _key, Transform _parent = null)
        {
            if (!handle_Dictionary.TryGetValue(_key, out AsyncOperationHandle handle))
            {
                Utils.CreateLogError<AddressableAssetController>($"Instantiate 실패: Load되지 않은 Key입니다. ({_key})");
                return null;
            }

            if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded)
            {
                Utils.CreateLogError<AddressableAssetController>($"Instantiate 실패: 유효하지 않은 Handle입니다. ({_key})");
                return null;
            }

            if (handle.Result is not GameObject prefab)
            {
                Utils.CreateLogError<AddressableAssetController>($"Instantiate 실패: GameObject 타입이 아닙니다. ({_key})");
                return null;
            }

            return _parent == null
                ? UnityEngine.Object.Instantiate(prefab)
                : UnityEngine.Object.Instantiate(prefab, _parent);
        }


        /// <summary>
        /// Instantiate로 생성된 인스턴스를 해제(Destroy)한다.
        /// 원본 Asset의 Addressable Handle은 handle_Dictionary가 별도로 관리하므로,
        /// 원본 Asset 자체를 Release하려면 Release(AddressKey)를 호출해야 한다.
        /// </summary>
        public void ReleaseInstance(GameObject _instance)
        {
            if (_instance == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_instance);
        }


        #endregion
    }
}
