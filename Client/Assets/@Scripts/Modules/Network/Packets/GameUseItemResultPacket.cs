namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CUseItemResult.cs의 UseItemFailReason과 같은 번호를 써야 한다.
    public enum GameUseItemFailReason : byte
    {
        None = 0,          // 성공
        Cooldown = 1,      // 재사용 대기시간 중(모든 물약이 공유)
        NotUsable = 2,     // 회복 아이템이 아니거나 써도 효과가 없는 상태(사망/만피)
        Rejected = 3,      // 서버 처리 실패(미보유/저장 실패 등) - 아이템은 소모되지 않았다
        TooFast = 4        // 요청 간격이 너무 짧다(연타 방지)
    }

    // 서버 Shared/Networking/Packets/S2CUseItemResult.cs와 형식이 동일해야 한다.
    // Success면 서버에서 아이템 1개가 실제로 차감됐다는 뜻이라 이때만 인벤토리 수량을 줄인다.
    // 회복된 체력은 이 패킷이 아니라 Game_PlayerHpBroadcast로 따로 온다.
    // CooldownRemainingMs는 성공/실패와 무관하게 "지금 남은 물약 재사용 대기시간"이다(성공하면 방금 시작된 대기시간,
    // Cooldown으로 거부되면 남은 시간, 대기 중이 아니면 0) - 사용 버튼을 잠그고 카운트다운하는 데 쓴다.
    public class GameUseItemResultPacket
    {
        public string ItemId;
        public bool Success;
        public GameUseItemFailReason FailReason;
        public int CooldownRemainingMs;

        public static GameUseItemResultPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameUseItemResultPacket
        {
            ItemId = reader.ReadString(),
            Success = reader.ReadBoolean(),
            FailReason = (GameUseItemFailReason)reader.ReadByte(),
            CooldownRemainingMs = reader.ReadInt32()
        });
    }
}
