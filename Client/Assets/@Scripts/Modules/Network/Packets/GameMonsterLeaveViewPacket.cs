namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CMonsterLeaveView.cs와 형식이 동일해야 한다.
    // 몬스터가 내 관심 영역(시야) 밖으로 나갔을 때 온다. 죽은 게 아니므로 사망 연출 없이 바로 지운다.
    public class GameMonsterLeaveViewPacket
    {
        public long MonsterId;

        public static GameMonsterLeaveViewPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
            new GameMonsterLeaveViewPacket { MonsterId = reader.ReadInt64() });
    }
}
