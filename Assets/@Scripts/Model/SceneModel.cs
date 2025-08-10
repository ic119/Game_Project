using System;
using System.Collections.Generic;

namespace JJORY.Model
{
    /// <summary>
    /// SceneLoad에 대한 Model
    /// </summary>
    [Serializable]
    public class SceneModel
    {
        public string sceneTag;
        public List<string> loadScenes;
        public string activeScene;
    }
}