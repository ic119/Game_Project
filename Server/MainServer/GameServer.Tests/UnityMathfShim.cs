// 클라이언트의 순수 계산 코드(Utils/ExpTable.cs 등)가 UnityEngine.Mathf를 쓰므로, 테스트 프로젝트에서 같은 동작의 최소 대역을 둔다.
// Unity의 Mathf.Pow는 double Math.Pow를 float로 줄인 값이고, Mathf.RoundToInt는 Math.Round(float)(짝수 반올림)이다.
namespace UnityEngine
{
    internal static class Mathf
    {
        public static float Pow(float f, float p) => (float)System.Math.Pow(f, p);

        public static int RoundToInt(float f) => (int)System.Math.Round(f);
    }
}
