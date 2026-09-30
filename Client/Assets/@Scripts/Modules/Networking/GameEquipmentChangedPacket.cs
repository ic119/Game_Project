namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CEquipmentChangedBroadcast.cs와 형식이 동일해야 한다.
    // 어떤 플레이어의 장착 장비(무기/갑옷/투구)가 바뀌었을 때 온다. 세 슬롯의 현재 값을 모두 담고(빈 문자열 = 빈 슬롯),
    // 받는 쪽은 이전 상태와 무관하게 그대로 덮어쓴다.
    public class GameEquipmentChangedPacket
    {
        public long PlayerId;
        public string WeaponItemId;
        public string ArmorItemId;
        public string HelmetItemId;

        public static GameEquipmentChangedPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameEquipmentChangedPacket
        {
            PlayerId = reader.ReadInt64(),
            WeaponItemId = reader.ReadString(),
            ArmorItemId = reader.ReadString(),
            HelmetItemId = reader.ReadString()
        });
    }
}
