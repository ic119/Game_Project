namespace Shared.Networking.Packets
{
    // 인벤토리에서 장비를 장착/해제했을 때 GameServer에 알린다. 서버는 AttackPower/Defense(클라이언트 자기 계산값)를 믿지 않고
    // 재계산 트리거로만 쓴다 - MainServer에서 캐릭터를 다시 조회해 공격력/방어력과 외형 장비(Game_EquipmentChangedBroadcast)를 갱신한다.
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
