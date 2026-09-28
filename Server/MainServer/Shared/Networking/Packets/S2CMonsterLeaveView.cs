namespace Shared.Networking.Packets
{
    // 몬스터가 받는 사람의 관심 영역(시야) 밖으로 나갔을 때 보낸다(GameRoom.Visibility). 죽은 게 아니므로 클라이언트는
    // 사망 연출(Game_MonsterDieBroadcast) 없이 바로 지운다. 다시 시야에 들어오면 Game_MonsterSpawnBroadcast로 전체 정보가 온다.
    public class S2CMonsterLeaveView
    {
        public long MonsterId { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer => writer.Write(MonsterId));

        public static S2CMonsterLeaveView Decode(byte[] body) =>
            BinaryPacket.Read(body, reader => new S2CMonsterLeaveView { MonsterId = reader.ReadInt64() });
    }
}
