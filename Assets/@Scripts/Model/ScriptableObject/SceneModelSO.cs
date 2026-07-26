using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Model.SO
{
    [CreateAssetMenu(fileName = "SceneModelSO", menuName = "ScriptableObjectAssets/SceneModelData")]
    public class SceneModelSO : ScriptableObject
    {
        public List<SceneModel> scneneModel_List;
    }
}    