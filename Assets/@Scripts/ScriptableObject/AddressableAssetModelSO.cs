using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Models.SO
{
    [CreateAssetMenu(fileName = "AddressableAssetModelSO", menuName = "ScriptableObjectAssets/AddressableAssetModel")]
    public class AddressableAssetModelSO : ScriptableObject
    {
        public List<AddressableAssetModel> addressableAssetModels;
    }
}