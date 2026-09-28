using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Utils
{
    /// <summary>
    /// 원격 개체(다른 플레이어/몬스터) 하나의 서버 스냅샷 위치를 서버 시각과 함께 쌓아 두고, "현재 서버 시각 - InterpolationDelayMs"
    /// 시점의 위치를 앞뒤 두 샘플 사이에서 보간해 돌려준다. 예전처럼 최신 위치로 매 프레임 Lerp하면 패킷 도착 간격이 흔들릴 때
    /// 움직임이 끊기고 속도가 들쭉날쭉했다 - 약간 과거를 그리는 대신, 항상 실제로 받은 두 위치 사이를 일정한 속도로 이동한다.
    /// 서버는 움직인 개체만 스냅샷에 담으므로(GameRoom 틱), 오래 멈춰 있다 움직이기 시작하면 "멈춰 있던 위치" 샘플을 새 샘플
    /// 바로 앞에 끼워 넣는다 - 그렇지 않으면 멈춰 있던 긴 시간 동안 천천히 미끄러져 온 것처럼 보인다.
    /// </summary>
    public class SnapshotInterpolationBuffer
    {
        /// <summary>
        /// 몇 ms 과거를 그릴지. 서버 틱(50ms) 두 번 분량이라 스냅샷 하나가 늦거나 빠져도 보통은 다음 샘플이 이미 와 있다.
        /// </summary>
        public const double InterpolationDelayMs = 100.0;

        // 서버 틱 간격(GameRoom.TickInterval과 같게 유지). 끼워 넣는 "멈춤" 샘플을 새 샘플보다 이만큼 앞에 둔다.
        private const double ServerTickMs = 50.0;

        // 직전 샘플과 이 이상 벌어지면 그 사이 멈춰 있었던 것으로 본다(틱 몇 번 분량).
        private const double IdleGapMs = 150.0;

        private const int MaxSamples = 32;

        private readonly struct Entry
        {
            public readonly double TimeMs;
            public readonly Vector3 Position;
            public readonly float RotationY;

            public Entry(double timeMs, Vector3 position, float rotationY)
            {
                TimeMs = timeMs;
                Position = position;
                RotationY = rotationY;
            }
        }

        private readonly List<Entry> samples = new();

        // 스폰/순간이동 직후처럼 아직 스냅샷 샘플이 없을 때 쓰는 현재 위치.
        private Vector3 restPosition;
        private float restRotationY;

        /// <summary>
        /// 스폰/부활처럼 보간 없이 즉시 옮길 때 호출한다. 쌓인 샘플을 버리고 이 위치에 멈춰 있는 상태로 되돌린다.
        /// </summary>
        public void Reset(Vector3 position, float rotationY)
        {
            samples.Clear();
            restPosition = position;
            restRotationY = rotationY;
        }

        public void Add(double serverTimeMs, Vector3 position, float rotationY)
        {
            if (samples.Count > 0)
            {
                Entry last = samples[samples.Count - 1];

                // 순서가 뒤바뀌었거나 중복인 스냅샷은 버린다.
                if (serverTimeMs <= last.TimeMs)
                {
                    return;
                }

                if (serverTimeMs - last.TimeMs > IdleGapMs)
                {
                    samples.Add(new Entry(serverTimeMs - ServerTickMs, last.Position, last.RotationY));
                }
            }
            else
            {
                // 멈춰 있던(또는 방금 스폰된) 자리에서 출발하도록 시작점을 둔다.
                samples.Add(new Entry(serverTimeMs - ServerTickMs, restPosition, restRotationY));
            }

            samples.Add(new Entry(serverTimeMs, position, rotationY));

            if (samples.Count > MaxSamples)
            {
                samples.RemoveRange(0, samples.Count - MaxSamples);
            }
        }

        /// <summary>
        /// renderTimeMs(서버 시각 기준) 시점의 위치/회전을 구한다. 가장 최신 샘플보다 뒤면 그 자리에 멈춰 있는다(추측해서 앞질러 가지 않는다).
        /// </summary>
        public void Sample(double renderTimeMs, out Vector3 position, out float rotationY)
        {
            if (samples.Count == 0)
            {
                position = restPosition;
                rotationY = restRotationY;
                return;
            }

            if (renderTimeMs <= samples[0].TimeMs)
            {
                position = samples[0].Position;
                rotationY = samples[0].RotationY;
                return;
            }

            Entry newest = samples[samples.Count - 1];
            if (renderTimeMs >= newest.TimeMs)
            {
                position = newest.Position;
                rotationY = newest.RotationY;
                return;
            }

            int index = 0;
            while (samples[index + 1].TimeMs < renderTimeMs)
            {
                index++;
            }

            // 이미 지나간 구간의 샘플은 버린다(현재 구간의 시작 샘플은 남긴다).
            if (index > 0)
            {
                samples.RemoveRange(0, index);
                index = 0;
            }

            Entry from = samples[index];
            Entry to = samples[index + 1];
            float t = (float)((renderTimeMs - from.TimeMs) / (to.TimeMs - from.TimeMs));
            position = Vector3.Lerp(from.Position, to.Position, t);
            rotationY = Mathf.LerpAngle(from.RotationY, to.RotationY, t);
        }
    }
}
