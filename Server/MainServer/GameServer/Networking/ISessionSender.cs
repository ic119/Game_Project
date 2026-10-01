using Shared.Networking;

namespace GameServer.Networking
{
    // GameRoom이 접속자에게 프레임을 보내는 데 필요한 것만 뽑은 추상화. 예전에는 GameRoom이 ClientSession 구체 타입을
    // 들고 있어 소켓 없이는 방을 만들어 볼 수 없었다 - 이 인터페이스 덕분에 테스트에서 가짜 전송기로 입장/전투/브로드캐스트
    // 흐름을 검증할 수 있다. ClientSession 구현은 대기열에 넣기만 하고 즉시 반환한다(어느 스레드에서 불러도 안전).
    public interface ISessionSender
    {
        void Send(OpCode opCode, byte[] body);
    }
}
