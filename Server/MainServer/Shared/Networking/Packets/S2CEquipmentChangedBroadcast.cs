namespace Shared.Networking.Packets
{
    // 어떤 플레이어의 장착 장비(무기/갑옷/투구)가 바뀌었을 때 그 플레이어를 보고 있는 사람과 본인에게 보낸다(Game_EquipmentChangedBroadcast).
    // 바뀐 슬롯만이 아니라 세 슬롯의 현재 값을 모두 담는다 - 받는 쪽이 이전 상태를 몰라도(막 시야에 들어온 경우 등) 그대로 덮어쓰면 된다.
    // 빈 문자열은 그 슬롯이 비어 있다는 뜻이다. 입장/시야 진입 시에는 PlayerInfo에 같은 값이 실려 온다.
    public class S2CEquipmentChangedBroadcast
    {
        public long PlayerId { get; set; }
        public string WeaponItemId { get; set; } = string.Empty;
        public string ArmorItemId { get; set; } = string.Empty;
        public string HelmetItemId { get; set; } = string.Empty;

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write(WeaponItemId);
            writer.Write(ArmorItemId);
            writer.Write(HelmetItemId);
        });

        public static S2CEquipmentChangedBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CEquipmentChangedBroadcast
        {
            PlayerId = reader.ReadInt64(),
            WeaponItemId = reader.ReadString(),
            ArmorItemId = reader.ReadString(),
            HelmetItemId = reader.ReadString()
        });
    }
}
