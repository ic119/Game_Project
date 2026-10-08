namespace Shared.Networking.Packets
{
    // 캐릭터 머리 위/몸에 재생하는 연출의 종류. 값은 클라이언트 GamePlayerEffectType과 같아야 한다 - 새 연출을 추가할 때는 번호를 재사용하지 말고 뒤에 붙인다.
    public enum PlayerEffectType : byte
    {
        None = 0,
        LevelUp = 1,
        HpPotion = 2
    }

    // 다른 플레이어의 캐릭터 연출 알림(S2C). 서버가 레벨업/물약 회복을 확정했을 때 playerId를 보고 있는 사람에게만 중계한다
    // (본인은 경험치 패킷/물약 사용 결과로 이미 로컬에서 재생하므로 받지 않는다). 효과 자체(체력 회복, 레벨 반영)는 다른 패킷이 전하고,
    // 이 패킷은 원격 화면의 이펙트 재생에만 쓰인다.
    public class S2CPlayerEffectBroadcast
    {
        public long PlayerId { get; set; }
        public PlayerEffectType Effect { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(PlayerId);
            writer.Write((byte)Effect);
        });

        public static S2CPlayerEffectBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CPlayerEffectBroadcast
        {
            PlayerId = reader.ReadInt64(),
            Effect = (PlayerEffectType)reader.ReadByte()
        });
    }
}
