using UnityEngine;

namespace Incheol.Presenter.Scene
{
    /// <summary>
    /// 물약 사용의 클라이언트 쪽 상태: 요청을 보내고 결과를 기다리는 중인지, 재사용 대기시간이 언제 끝나는지.
    /// 실제 판정은 서버가 하고(모든 물약이 하나의 대기시간을 공유), 이 상태는 어차피 거부될 요청을 미리 거르고 남은 시간을 보여주는 데 쓴다.
    /// </summary>
    public class PotionUseState
    {
        // 서버가 Game_UseItemResult로 알려준 남은 시간으로 갱신하는, 대기시간이 끝나는 시각(Time.unscaledTime 기준).
        private float readyAtTime;

        /// <summary>
        /// 물약 사용 요청(Game_UseItemRequest)을 보내고 결과를 기다리는 중이면 true.
        /// 응답 전 연타로 같은 요청이 여러 번 나가지 않게 막는다.
        /// </summary>
        public bool IsPending { get; set; }

        /// <summary>다음 물약을 쓸 수 있기까지 남은 시간(초). 대기 중이 아니면 0.</summary>
        public float RemainingSeconds => Mathf.Max(0f, readyAtTime - Time.unscaledTime);

        /// <summary>서버가 알려준 남은 대기시간(ms)으로 갱신한다.</summary>
        public void SetCooldownMs(int remainingMs)
        {
            readyAtTime = Time.unscaledTime + remainingMs / 1000f;
        }

        /// <summary>대기 상태와 대기시간을 모두 비운다(재접속하면 서버의 물약 대기시간도 초기화된다).</summary>
        public void Reset()
        {
            IsPending = false;
            readyAtTime = 0f;
        }
    }
}
