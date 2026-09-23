namespace Shared.Networking.Packets
{
    // 인벤토리에서 장비를 장착/해제해 공격력/방어력이 바뀌었을 때 GameServer에 알린다. Game_EnterRequest 때와
    // 동일한 신뢰 수준(Nickname과 같은 수준 - 서버가 별도로 검증하지 않고 그대로 캐싱)으로 클라이언트 값을 반영한다.
    public class C2SStatUpdateRequest
    {
        public long PlayerId { get; set; }
        public int AttackPower { get; set; }
        public int Defense { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(AttackPower);
            writer.Write(Defense);
        });

        public static C2SStatUpdateRequest Decode(byte[] body) => BinaryPacket.Read(body, reader => new C2SStatUpdateRequest
        {
            PlayerId = reader.ReadInt64(),
            AttackPower = reader.ReadInt32(),
            Defense = reader.ReadInt32()
        });
    }
}
