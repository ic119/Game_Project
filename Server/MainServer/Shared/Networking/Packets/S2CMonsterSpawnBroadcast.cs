namespace Shared.Networking.Packets
{
    // 리스폰으로 새 몬스터 개체 하나가 태어났을 때 브로드캐스트한다. 입장 시점의 기존 몬스터 스냅샷은
    // S2CEnterAck.ExistingMonsters로 별도 전달되므로, 이 패킷은 "룸이 이미 만들어진 뒤 새로 태어난" 경우만 다룬다.
    public class S2CMonsterSpawnBroadcast
    {
        public MonsterInfo Monster { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer => Monster.WriteTo(writer));

        public static S2CMonsterSpawnBroadcast Decode(byte[] body) =>
            BinaryPacket.Read(body, reader => new S2CMonsterSpawnBroadcast { Monster = MonsterInfo.ReadFrom(reader) });
    }
}
