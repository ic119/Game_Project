using System.Threading;

namespace GameServer.Monsters
{
    // 몬스터 id는 방(mapId)마다가 아니라 서버 전체에서 유일해야 한다 - 클라이언트가 여러 방(맵)을 오갈 때도
    // 서로 다른 몬스터를 같은 id로 착각하지 않도록. 플레이어 id(DB characterId)와는 별도 채번 공간이라
    // Game_MonsterAttackRequest/Game_AttackRequest가 애초에 다른 OpCode로 분리되어 있어 겹쳐도 문제없다.
    public static class MonsterIdGenerator
    {
        private static long _nextId;

        public static long Next() => Interlocked.Increment(ref _nextId);
    }
}
