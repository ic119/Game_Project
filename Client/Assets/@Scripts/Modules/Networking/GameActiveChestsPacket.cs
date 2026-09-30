using System.Collections.Generic;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/S2CActiveChests.cs의 ActiveChestInfo와 필드 순서가 일치해야 한다.
    public class GameChestInfo
    {
        // TreasureChestInteractionController.chestId와 같은 값(후보에서 뽑힌 상자는 후보 마커 GameObject 이름).
        public string Id;
        public float X;
        public float Y;
        public float Z;

        // Drops/DropTables.json 키(Define.ChestLootTableKey 멤버 이름과 같다).
        public string LootTableKey;
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
                chests.Add(new GameChestInfo
                {
                    Id = reader.ReadString(),
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle(),
                    Z = reader.ReadSingle(),
                    LootTableKey = reader.ReadString()
                });
            }

            return new GameActiveChestsPacket { Chests = chests };
        });
    }
}
