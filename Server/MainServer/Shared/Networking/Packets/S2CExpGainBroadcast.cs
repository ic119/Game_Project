namespace Shared.Networking.Packets
{
    // 몬스터를 처치한 플레이어 본인에게만(유니캐스트) 전송된다 - 방 전체에 뿌릴 이유가 없다.
    // TotalExp/Level/ExpToNextLevel은 서버가 ExpTable로 계산한 최종 상태를 그대로 실어 보내므로,
    // 클라이언트는 델타를 누적하지 않고 이 값으로 덮어쓰면 된다(S2CMonsterDamageBroadcast.RemainingHp와 같은 이유).
    public class S2CExpGainBroadcast
    {
        public long MonsterId { get; set; }
        public int GainedExp { get; set; }
        public int TotalExp { get; set; }
        public int Level { get; set; }
        public bool DidLevelUp { get; set; }

        // 만렙(ExpTable.MaxLevel) 도달 시 0.
        public int ExpToNextLevel { get; set; }

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(MonsterId);
            writer.Write(GainedExp);
            writer.Write(TotalExp);
            writer.Write(Level);
            writer.Write(DidLevelUp);
            writer.Write(ExpToNextLevel);
        });

        public static S2CExpGainBroadcast Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CExpGainBroadcast
        {
            MonsterId = reader.ReadInt64(),
            GainedExp = reader.ReadInt32(),
            TotalExp = reader.ReadInt32(),
            Level = reader.ReadInt32(),
            DidLevelUp = reader.ReadBoolean(),
            ExpToNextLevel = reader.ReadInt32()
        });
    }
}
