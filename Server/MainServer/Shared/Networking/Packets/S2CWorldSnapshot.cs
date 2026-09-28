namespace Shared.Networking.Packets
{
    // GameRoom 틱(20Hz)마다 한 번, 직전 틱 이후 위치가 바뀐 플레이어/몬스터를 모아 받는 사람당 패킷 하나로 보낸다.
    // 예전에는 이동 요청 하나마다(플레이어) / AI 틱마다 몬스터 하나마다 개별 패킷(Game_MoveBroadcast/Game_MonsterMoveBroadcast)을
    // 맵 전체에 보내 패킷 수가 인원수의 제곱으로 늘었다. 전투/HP/입장·퇴장 같은 이벤트성 알림은 지금도 즉시 따로 보낸다.
    // ServerTimeMs는 스냅샷을 만든 서버 시각(Unix ms) - 클라이언트 보간 기준으로 쓴다.
    public class S2CWorldSnapshot
    {
        public long ServerTimeMs { get; set; }
        public List<EntityTransform> Players { get; set; } = new();
        public List<EntityTransform> Monsters { get; set; } = new();

        public byte[] Encode() => BinaryPacket.Write(writer =>
        {
            writer.Write(ServerTimeMs);
            WriteList(writer, Players);
            WriteList(writer, Monsters);
        });

        public static S2CWorldSnapshot Decode(byte[] body) => BinaryPacket.Read(body, reader => new S2CWorldSnapshot
        {
            ServerTimeMs = reader.ReadInt64(),
            Players = ReadList(reader),
            Monsters = ReadList(reader)
        });

        private static void WriteList(BinaryWriter writer, List<EntityTransform> entities)
        {
            writer.Write(entities.Count);
            foreach (EntityTransform entity in entities)
            {
                writer.Write(entity.Id);
                writer.Write(entity.X);
                writer.Write(entity.Y);
                writer.Write(entity.Z);
                writer.Write(entity.RotationY);
            }
        }

        private static List<EntityTransform> ReadList(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var entities = new List<EntityTransform>(count);
            for (int i = 0; i < count; i++)
            {
                entities.Add(new EntityTransform(reader.ReadInt64(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
            }
            return entities;
        }
    }

    // 스냅샷 한 항목(플레이어 id 또는 몬스터 id + 위치/회전).
    public readonly record struct EntityTransform(long Id, float X, float Y, float Z, float RotationY);
}
