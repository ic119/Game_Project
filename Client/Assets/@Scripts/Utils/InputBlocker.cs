using System.Collections.Generic;
using UnityEngine;

namespace Incheol.Utils
{
    /// <summary>
    /// 게임플레이 키 입력(이동/대쉬/공격/단축키/상호작용)을 막아야 하는 상황을 한곳에서 알려주는 정적 게이트.
    /// 채팅 입력창에 글자를 치는 동안 "c"가 공격으로, "i"가 인벤토리로, 방향키/Space가 이동/대쉬로 해석되면 안 되는 것이 대표적이다.
    /// 입력을 읽는 쪽(PlayerMoveController 등)은 채팅 UI 같은 특정 화면을 몰라도 되고 IsBlocked만 확인하면 된다 -
    /// 막는 쪽도 컨트롤러를 직접 호출하지 않는다.
    ///
    /// 막는 주체(owner)별로 따로 등록하므로 여러 곳이 동시에 막아도(예: 채팅 입력 중 + 이후 추가될 팝업) 한쪽이 풀렸다고
    /// 전체가 풀리지 않고, 모든 주체가 풀려야 IsBlocked가 false가 된다. 주체가 파괴/비활성화될 때는 반드시 SetBlocked(owner, false)로
    /// 풀어야 한다(풀지 않으면 입력이 계속 막힌다).
    /// 메인 스레드에서만 쓴다.
    /// </summary>
    public static class InputBlocker
    {
        private static readonly HashSet<object> blockers = new();

        /// <summary>하나라도 입력을 막고 있으면 true.</summary>
        public static bool IsBlocked => blockers.Count > 0;

        /// <summary>
        /// owner가 입력을 막거나(blocked=true) 풀어준다. 같은 owner가 여러 번 막아도 한 번으로 취급한다.
        /// </summary>
        public static void SetBlocked(object owner, bool blocked)
        {
            if (owner == null)
            {
                return;
            }

            if (blocked)
            {
                blockers.Add(owner);
            }
            else
            {
                blockers.Remove(owner);
            }
        }

        // "Enter Play Mode Options"에서 도메인 리로드를 끄면 정적 값이 플레이 사이에 남는다 - 이전 플레이에서 파괴된 주체가
        // 풀어주지 못한 채 남아 다음 플레이 시작부터 입력이 막히는 일을 막기 위해 플레이 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart()
        {
            blockers.Clear();
        }
    }
}
