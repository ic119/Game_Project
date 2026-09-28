using UnityEngine;

namespace Incheol.Modules.Networking
{
    /// <summary>
    /// 스냅샷(Game_WorldSnapshot)에 담긴 서버 시각으로 "지금 서버 시각"을 추정한다. 원격 개체 보간
    /// (SnapshotInterpolationBuffer)이 서버 시각 기준으로 "조금 과거"를 그리기 위해 쓴다.
    /// 수신 시점의 (서버 시각 - 로컬 시각)은 실제 시계 차이에서 네트워크 지연만큼 작게 나오므로, 지연이 작은 샘플(값이 큰 쪽)은
    /// 빠르게 받아들이고 지연이 큰 샘플(값이 작은 쪽)은 천천히 따라가 "가장 빠르게 도착한 경우"에 가깝게 맞춘다.
    /// 메인 스레드에서만 쓴다(GameServerConnectManager가 스냅샷 이벤트를 메인 스레드로 넘긴 뒤 Observe한다).
    /// </summary>
    public static class ServerClock
    {
        // 이만큼 크게 어긋나면(서버 재시작, 오래 끊김 등) 천천히 따라가지 않고 바로 맞춘다.
        private const double ResyncThresholdMs = 500.0;
        private const double FasterSampleWeight = 0.5;
        private const double SlowerSampleWeight = 0.05;

        private static double offsetMs;
        private static bool hasOffset;

        public static bool IsSynced => hasOffset;

        /// <summary>추정한 현재 서버 시각(Unix ms). 동기화 전이면 로컬 시각 그대로다.</summary>
        public static double NowMs => LocalMs + offsetMs;

        private static double LocalMs => Time.realtimeSinceStartupAsDouble * 1000.0;

        /// <summary>새 연결을 시작할 때 이전 연결의 추정값을 버린다.</summary>
        public static void Reset()
        {
            hasOffset = false;
            offsetMs = 0.0;
        }

        public static void Observe(long serverTimeMs)
        {
            double sample = serverTimeMs - LocalMs;

            if (!hasOffset || System.Math.Abs(sample - offsetMs) > ResyncThresholdMs)
            {
                offsetMs = sample;
                hasOffset = true;
                return;
            }

            double weight = sample > offsetMs ? FasterSampleWeight : SlowerSampleWeight;
            offsetMs += (sample - offsetMs) * weight;
        }
    }
}
