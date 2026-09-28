using System.Collections.Generic;
using System.IO;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CWorldSnapshot.cs와 형식이 동일해야 한다.
    // 서버 방 틱(20Hz)마다, 직전 틱 이후 위치가 바뀐 플레이어/몬스터가 한 패킷에 묶여 온다(예전의 Game_MoveBroadcast/
    // Game_MonsterMoveBroadcast를 대체). 내 캐릭터도 목록에 들어 있을 수 있지만 원격 목록에 없으므로 자연히 무시된다.
    // ServerTimeMs는 서버가 스냅샷을 만든 시각(Unix ms).
    public class GameWorldSnapshotPacket
    {
        public long ServerTimeMs;
        public List<GameEntityTransform> Players = new();
        public List<GameEntityTransform> Monsters = new();

        public static GameWorldSnapshotPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameWorldSnapshotPacket
        {
            ServerTimeMs = reader.ReadInt64(),
            Players = ReadList(reader),
            Monsters = ReadList(reader)
        });

        private static List<GameEntityTransform> ReadList(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var entities = new List<GameEntityTransform>(count);
            for (int i = 0; i < count; i++)
            {
                entities.Add(new GameEntityTransform
                {
                    Id = reader.ReadInt64(),
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle(),
                    Z = reader.ReadSingle(),
                    RotationY = reader.ReadSingle()
                });
            }
            return entities;
        }
    }

    // 스냅샷 한 항목(플레이어 id 또는 몬스터 id + 위치/회전).
    public struct GameEntityTransform
    {
        public long Id;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;
    }
}
