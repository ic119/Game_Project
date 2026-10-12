using System.Collections.Generic;
using System.IO;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CActiveChests.cs의 ActiveChestInfo와 필드 순서가 일치해야 한다.
    // Game_ActiveChestsNotify의 목록 항목으로도, Game_ChestSpawnBroadcast(리스폰)의 바디로도 재사용된다.
    public class GameChestInfo
    {
        // TreasureChestInteractionController.chestId와 같은 값(후보에서 뽑힌 상자는 후보 마커 GameObject 이름).
        public string Id;
        public float X;
        public float Y;
        public float Z;

        // Drops/DropTables.json 키(Define.ChestLootTableKey 멤버 이름과 같다).
        public string LootTableKey;

        public static GameChestInfo ReadFrom(BinaryReader reader) => new GameChestInfo
        {
            Id = reader.ReadString(),
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            LootTableKey = reader.ReadString()
        };
    }

    // 서버 Shared/Networking/Packets/S2CActiveChests.cs와 형식이 동일해야 한다.
    // Game_EnterAck/Game_MapChangeAck 직후, 이미 열린 상자를 따라잡는 Game_ChestOpenBroadcast보다 먼저 온다.
    // 고정 상자(씬에 이미 있는 오브젝트)와 방 생성 시 후보에서 뽑힌 상자가 함께 들어 있다.
    public class GameActiveChestsPacket
    {
        public List<GameChestInfo> Chests = new();

        public static GameActiveChestsPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader =>
        {
            int count = reader.ReadInt32();
            var chests = new List<GameChestInfo>(count);
            for (int i = 0; i < count; i++)
            {
                chests.Add(GameChestInfo.ReadFrom(reader));
            }

            return new GameActiveChestsPacket { Chests = chests };
        });
    }

    // 서버 Shared/Networking/Packets/S2CChestSpawnBroadcast.cs와 형식이 동일해야 한다(리스폰으로 새 상자가 생김).
    public class GameChestSpawnPacket
    {
        public GameChestInfo Chest;

        public static GameChestSpawnPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameChestSpawnPacket
        {
            Chest = GameChestInfo.ReadFrom(reader)
        });
    }

    // 서버 Shared/Networking/Packets/S2CChestDespawnBroadcast.cs와 형식이 동일해야 한다(열린 뒤 잔존 시간이 지난 상자가 사라짐).
    public class GameChestDespawnPacket
    {
        public string ChestId;

        public static GameChestDespawnPacket Decode(byte[] body) => GameBinaryPacket.Read(body, reader => new GameChestDespawnPacket
        {
            ChestId = reader.ReadString()
        });
    }
}
