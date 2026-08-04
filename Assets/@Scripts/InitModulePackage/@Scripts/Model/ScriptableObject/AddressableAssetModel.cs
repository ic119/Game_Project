using ModulePackage.Utils;
using System;
using System.Collections.Generic;

[Serializable]
public class AddressableAssetModel
{
    /// <summary>
    /// SceneDataModel.tags와 매칭되는 태그 (예: main)
    /// </summary>
    public string tags;

    /// <summary>
    /// 해당 태그 씬 전환 시 LoadingScene 구간에서 미리 로드할 Addressable Key 목록
    /// </summary>
    public List<AddressableKey> preloadAddressableKeys = new List<AddressableKey>();
}
