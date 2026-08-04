using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AddressableAssetModelSO", menuName = "ScriptableObjectAssets/AddressableAssetModel")]
public class AddressableAssetModelSO : ScriptableObject
{
    public List<AddressableAssetModel> addressableAssetModels;
}
