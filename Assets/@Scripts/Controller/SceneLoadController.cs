using JJORY.Model;
using JJORY.Util;
using Photon.Pun.Demo.PunBasics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JJORY.Module
{
    public class SceneLoadController : SingletonObject<SceneLoadController>
    {
        #region Variable
        [SerializeField] private SceneModel cur_SceneModel;
        private Dictionary<string, SceneModel> sceneModel_Dictionary = new Dictionary<string, SceneModel>();

        [Range(0.0f, 2.0f)]
        [SerializeField] private float delayTime;
        [SerializeField] private string address = "Load_Scene";

        public float cur_LoadProgress { get; private set; } = 0.0f;
        public bool isLoading { get; private set; } = false;
        #endregion

        #region Method
        public void Init(Action _onReady)
        {
            
        }
        #endregion
    }
}