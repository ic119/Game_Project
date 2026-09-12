using System;
using System.IO;
using System.Text;

namespace Incheol.Modules.Networking
{
    // 서버 Shared/Networking/Packets/PlayerInfo.cs와 필드 순서가 반드시 일치해야 한다.
    // Game_EnterRequest(C2S)의 바디로도, Game_EnterAck/Game_PlayerJoined 안의 항목으로도 재사용된다.
    public class GamePlayerInfo
    {
        public long PlayerId;
        public string Nickname = string.Empty;
        public int HairIndex;
        public int EyeIndex;
        public int MouthIndex;
        public float X;
        public float Y;
        public float Z;
        public float RotationY;

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(PlayerId);
            writer.Write(Nickname);
            writer.Write(HairIndex);
            writer.Write(EyeIndex);
            writer.Write(MouthIndex);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(RotationY);
        }

        public static GamePlayerInfo ReadFrom(BinaryReader reader)
        {
            return new GamePlayerInfo
            {
                PlayerId = reader.ReadInt64(),
                Nickname = reader.ReadString(),
                HairIndex = reader.ReadInt32(),
                EyeIndex = reader.ReadInt32(),
                MouthIndex = reader.ReadInt32(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
                RotationY = reader.ReadSingle()
            };
        }

        public byte[] Encode()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            WriteTo(writer);
            writer.Flush();
            return stream.ToArray();
        }

        public static GamePlayerInfo Decode(byte[] body)
        {
            using var stream = new MemoryStream(body);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            return ReadFrom(reader);
        }
    }
}
