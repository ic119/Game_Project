using MainServer.AuthServer.Entities;

namespace MainServer.CharacterServer.Entities
{
    public class Character
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string Nickname { get; set; } = null!;
        public int HairIndex { get; set; }
        public int EyeIndex { get; set; }
        public int MouthIndex { get; set; }
        public int Str { get; set; }
        public int Agi { get; set; }
        public int Intel { get; set; }
        public int Level { get; set; } = 1;
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public User User { get; set; } = null!;
    }
}
