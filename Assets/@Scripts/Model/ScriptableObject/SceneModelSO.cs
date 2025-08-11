using System.Collections.Generic;
using UnityEngine;

namespace JJORY.Model.SO
{
    [CreateAssetMenu(fileName = "SceneModelSO", menuName = "ScriptableObjectAssets/SceneModelData")]
    public class SceneModelSO : ScriptableObject
    {
        public List<SceneModel> scneneModel_List;
    }
}    