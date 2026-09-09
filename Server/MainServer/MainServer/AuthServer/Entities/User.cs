using System;
using System.Collections.Generic;
using MainServer.CharacterServer.Entities;

namespace MainServer.AuthServer.Entities
{
    public class User
    {
        public long Id { get; set; }
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Nickname { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // 계정의 슬롯 현황(보유 캐릭터 수 등)과 캐릭터 목록을 양방향으로 탐색할 수 있도록 연결한다.
        public CharacterSlot? CharacterSlot { get; set; }
        public ICollection<Character> Characters { get; set; } = new List<Character>();
    }
}