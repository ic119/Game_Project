namespace GameServer.Networking
{
    // 세션 하나의 특정 요청 종류에 대한 초당 허용 횟수 제한(토큰 버킷). 버킷은 초당 refillPerSecond개씩 차고 최대 capacity개까지
    // 쌓이며, 요청 하나가 1개를 쓴다 - 평소 빈도는 통과시키고 짧은 몰림(네트워크 지연)은 capacity만큼 허용하되, 도배만 막는다.
    // 방 전체로 브로드캐스트되는 요청(채팅/이동)은 한 명의 도배가 같은 방 모든 세션의 전송 대기열을 채워, 정상 클라이언트까지
    // "느린 클라이언트"로 끊기게 만들 수 있어 반드시 제한해야 한다(ClientSession.Send 참고). 한 세션의 수신 루프에서만 쓰므로 스레드 안전하지 않아도 된다.
    public class RequestRateLimiter
    {
        private readonly double _capacity;
        private readonly double _refillPerSecond;
        private double _tokens;
        private DateTime _lastRefillAtUtc = DateTime.UtcNow;

        public RequestRateLimiter(double capacity, double refillPerSecond)
        {
            _capacity = capacity;
            _refillPerSecond = refillPerSecond;
            _tokens = capacity;
        }

        public bool TryAcquire()
        {
            var now = DateTime.UtcNow;
            _tokens = Math.Min(_capacity, _tokens + (now - _lastRefillAtUtc).TotalSeconds * _refillPerSecond);
            _lastRefillAtUtc = now;

            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }
}
