using Incheol.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Modules
{
    public class SoundManager : SingletonObject<SoundManager>
    {
        #region Variable
        [Serializable]
        private class SoundClipData
        {
            public string Name;
            public AudioClip Clip;
            [Range(0f, 1f)]
            public float Volume = 1f;
        }

        [SerializeField] private List<SoundClipData> soundClips = new List<SoundClipData>();

        private readonly Dictionary<string, AudioSource> soundSourceMap = new Dictionary<string, AudioSource>();
        #endregion

        #region LifeCycle
        /// <summary>
        /// 씬 전환에도 파괴되지 않도록 유지한다. false(기본값)로 두면 이 컨트롤러가 배치된 씬이
        /// 언로드될 때(예: Bootstrap → Loading 씬 전환) 함께 파괴되어, 이후 Instance 접근 시
        /// Inspector에 등록된 soundClips가 전혀 없는 빈 인스턴스가 새로 생성되어버린다.
        /// </summary>
        protected override bool PersistAcrossScenes => true;

        protected override void Awake()
        {
            base.Awake();

            if (Instance != this)
            {
                return;
            }

            BuildSoundSources();
        }
        #endregion

        #region Method
        /// <summary>
        /// Inspector에 등록된 soundClips 항목마다 전용 AudioSource를 만들어 붙인다.
        /// 이름으로 재생/정지를 구분해야 하므로, 하나의 AudioSource를 공유하지 않고 항목별로 분리한다.
        /// </summary>
        private void BuildSoundSources()
        {
            foreach (SoundClipData data in soundClips)
            {
                if (string.IsNullOrWhiteSpace(data.Name))
                {
                    DebugLogManager.GenerateErrorMessage<SoundManager>("이름이 비어 있는 SoundClip 항목이 있어 건너뜁니다.");
                    continue;
                }

                if (data.Clip == null)
                {
                    DebugLogManager.GenerateErrorMessage<SoundManager>($"'{data.Name}' 항목에 AudioClip이 연결되어 있지 않아 건너뜁니다.");
                    continue;
                }

                if (soundSourceMap.ContainsKey(data.Name))
                {
                    DebugLogManager.GenerateErrorMessage<SoundManager>($"'{data.Name}' 이름이 중복되어 있습니다. 첫 번째 항목만 사용됩니다.");
                    continue;
                }

                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.clip = data.Clip;
                source.volume = data.Volume;
                source.playOnAwake = false;

                soundSourceMap.Add(data.Name, source);
            }
        }

        /// <summary>
        /// Inspector에 등록된 이름의 사운드를 재생한다.
        /// _loop가 false(기본값)면 1회 재생, true면 반복 재생한다.
        /// 이미 재생 중이면 정지 후 처음부터 다시 재생한다.
        /// </summary>
        public void Play(string _name, bool _loop = false)
        {
            if (string.IsNullOrEmpty(_name))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>("사운드 이름이 비어 있습니다.");
                return;
            }

            if (!soundSourceMap.TryGetValue(_name, out AudioSource source))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>($"'{_name}' 사운드를 찾을 수 없습니다. Inspector에 등록된 이름인지 확인하세요.");
                return;
            }

            source.loop = _loop;
            source.Stop();
            source.Play();
        }

        /// <summary>
        /// Inspector에 등록된 이름의 사운드를 정지한다.
        /// </summary>
        public void Stop(string _name)
        {
            if (string.IsNullOrEmpty(_name))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>("사운드 이름이 비어 있습니다.");
                return;
            }

            if (!soundSourceMap.TryGetValue(_name, out AudioSource source))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>($"'{_name}' 사운드를 찾을 수 없습니다. Inspector에 등록된 이름인지 확인하세요.");
                return;
            }

            source.Stop();
        }

        /// <summary>
        /// 현재 재생 중인 모든 사운드를 정지한다.
        /// </summary>
        public void StopAll()
        {
            foreach (AudioSource source in soundSourceMap.Values)
            {
                source.Stop();
            }
        }

        /// <summary>
        /// Inspector에 등록된 이름의 사운드 음량을 조절한다. 재생 중에도 즉시 반영되며,
        /// _volume은 0~1 범위를 벗어나면 0~1로 잘라낸다(clamp).
        /// </summary>
        public void SetVolume(string _name, float _volume)
        {
            if (string.IsNullOrEmpty(_name))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>("사운드 이름이 비어 있습니다.");
                return;
            }

            if (!soundSourceMap.TryGetValue(_name, out AudioSource source))
            {
                DebugLogManager.GenerateErrorMessage<SoundManager>($"'{_name}' 사운드를 찾을 수 없습니다. Inspector에 등록된 이름인지 확인하세요.");
                return;
            }

            source.volume = Mathf.Clamp01(_volume);
        }
        #endregion
    }
}