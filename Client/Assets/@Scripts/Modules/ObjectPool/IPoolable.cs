namespace Incheol.Modules
{
    /// <summary>
    /// 풀링되는 프리팹의 컴포넌트가 구현하면, ObjectPoolManager가 Get()/Release() 시점에
    /// 자동으로 호출해주는 초기화/정리 훅. 재사용 시 컴포넌트별 상태(파티클, 속도 등)를
    /// 초기화하고 싶을 때 이 인터페이스를 구현하면 된다.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Get()으로 풀에서 대여되어 활성화된 직후 호출된다.</summary>
        void OnGetFromPool();

        /// <summary>Release()로 풀에 반환되어 비활성화되기 직전에 호출된다.</summary>
        void OnReleaseToPool();
    }
}
