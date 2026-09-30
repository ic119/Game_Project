namespace Shared.Networking.Packets
{
    // 물약 사용이 거부된 이유. 클라이언트가 "왜 안 되는지" 안내하는 데 쓴다. 값은 선언 순서가 아니라 번호로 주고받으므로
    // 기존 값을 바꾸거나 재사용하지 말고 끝에 추가한다(클라이언트 GameUseItemFailReason과 같은 번호).
    public enum UseItemFailReason : byte
    {
        None = 0,          // 성공(실패 사유 없음)
        Cooldown = 1,      // 재사용 대기시간 중(모든 물약이 공유)
        NotUsable = 2,     // 회복 아이템이 아니거나, 써도 효과가 없는 상태(사망/만피)
        Rejected = 3,      // 서버 처리 실패(미보유/저장 실패 등) - 아이템은 소모되지 않았다
        TooFast = 4        // 요청 간격이 너무 짧다(연타 방지)
    }

    // C2SUseItemRequest의 처리 결과(요청자 본인에게만). Success면 아이템 1개가 실제로 차감됐다는 뜻이라
    // 클라이언트는 이때만 인벤토리 수량을 줄인다. 실패(사망/만피/회복 아이템 아님/미보유/저장 실패/대기 중)면 아무 것도 바뀌지 않았다.
    // CooldownRemainingMs는 성공/실패와 무관하게 "지금 남은 물약 재사용 대기시간"이다 - 성공하면 방금 시작된 대기시간,
    // Cooldown으로 거부되면 남은 시간이 담기고, 대기 중이 아니면 0이다. 클라이언트는 이 값으로 사용 버튼을 잠그고 카운트다운한다.
    public class S2CUseItemResult
    {
        public string ItemId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public UseItemFailReason FailReason { get; set; } = UseItemFailReason.None;
        public int CooldownRemainingMs { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ItemId);
            writer.Write(Success);
            writer.Write((byte)FailReason);
            writer.Write(CooldownRemainingMs);
        });

        public static S2CUseItemResult Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CUseItemResult
        {
            ItemId = reader.ReadString(),
            Success = reader.ReadBoolean(),
            FailReason = (UseItemFailReason)reader.ReadByte(),
            CooldownRemainingMs = reader.ReadInt32()
        });
    }
}
