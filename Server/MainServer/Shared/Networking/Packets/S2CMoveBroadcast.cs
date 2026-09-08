using MessagePack;

namespace Shared.Networking.Packets
{
    [MessagePackObject]
    public class S2CMoveBroadcast
    {
        [Key(0)]
        public long PlayerId { get; set; }

        [Key(1)]
        public float X { get; set; }

        [Key(2)]
        public float Y { get; set; }

        [Key(3)]
        public long Timestamp { get; set; }
    }
}
