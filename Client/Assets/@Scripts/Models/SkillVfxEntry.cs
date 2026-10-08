using System;
using Incheol.Models.Define;
using UnityEngine;

namespace Incheol.Models.SO
{
    // 스킬 이펙트 하나(어떤 프리팹을 어디에 어떤 크기로 놓을지). key가 AddressableAssetKey.None이면 재생하지 않는다.
    [Serializable]
    public class SkillVfxLayer
    {
        public AddressableAssetKey key = AddressableAssetKey.None;

        [Tooltip("기준점에서의 로컬 오프셋(x 오른쪽, y 위, z 앞). 기준점의 방향은 시전자가 바라보는 방향이다.")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("프리팹 원래 크기(m)에 추가로 곱하는 배율. 1이면 그대로(또는 자동 크기 맞춤 결과 그대로).")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("범위 이펙트의 프리팹 원래 크기(m): 직선/부채꼴은 길이, 원은 지름. 0보다 크면 스킬 범위 크기에 맞춰 자동으로 크기를 키운다/줄인다(크기 = 범위 크기 / 이 값). 0이면 자동 크기 맞춤을 하지 않는다.")]
        [Min(0f)] public float referenceSize = 0f;

        [Tooltip("프리팹이 향하는 방향이 시전 방향과 다를 때 더하는 회전(도). 직선 이펙트가 옆으로 누워 있으면 90을 넣는다.")]
        public float yawOffset = 0f;
    }

    // SkillVfxDatabaseSO에 리스트로 등록되어 스킬 id(SkillDefinitions.json의 id)로 조회된다.
    [Serializable]
    public class SkillVfxEntry
    {
        public string skillId = string.Empty;

        [Tooltip("시전 후 기본공격을 막는 시간(초). 시전 잠금(서버 CastLockSeconds)보다 짧거나 0이면 시전 잠금까지만 막는다. 이펙트가 시전 모션보다 오래 이어지는 스킬은 이 값을 크게 해서 이펙트가 끝날 즈음까지 기본공격이 나가지 않게 한다. 대쉬로 시전을 끊으면 함께 풀린다.")]
        [Min(0f)] public float attackLockSeconds = 0f;

        [Tooltip("시전 순간 시전자 곁에서 재생한다(휘두르는 궤적, 시전 섬광 등). 기준점은 시전자 발 위치다.")]
        public SkillVfxLayer cast = new SkillVfxLayer();

        [Tooltip("시전 순간 범위 중심에서 재생한다(마법진 등 미리 보이는 연출). 기준점은 범위 중심이다.")]
        public SkillVfxLayer areaStart = new SkillVfxLayer();

        [Tooltip("타격 시각마다 범위 중심에서 재생한다(폭발, 낙뢰, 바닥 충격 등). 연타 스킬은 타격마다 재생한다. 기준점은 범위 중심이다.")]
        public SkillVfxLayer areaHit = new SkillVfxLayer();
    }
}
