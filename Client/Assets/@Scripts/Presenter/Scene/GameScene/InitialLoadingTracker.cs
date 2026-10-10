using Incheol.Modules;
using Incheol.Utils;
using UnityEngine;

namespace Incheol.Presenter.Scene
{
    /// <summary>
    /// 로비에서 시작 버튼으로 들어오면 SceneLoadManager가 프리로드를 100%까지 채운 뒤 GameScene으로 전환하고, 로딩바는 100%인 채로
    /// 넘겨준다. 맵/UI/플레이어 생성과 서버 입장은 GameScene에서 이어지므로, 이 클래스는 이어받은 로딩바의 제목만 바꿔 안내하고
    /// 서버 입장이 확인되면(또는 타임아웃/연결 실패 시) 숨기는 책임을 진다. GameScene을 에디터에서 바로 실행하는 등 로딩바가
    /// 없으면 모든 메서드가 아무 일도 하지 않는다.
    /// </summary>
    public class InitialLoadingTracker
    {
        // 입장 응답이 오지 않는 등 예상 밖의 이유로 로딩바가 화면을 영원히 가리지 않도록 하는 안전장치.
        private const float TimeoutSeconds = 30f;

        private readonly MonoBehaviour owner;
        private readonly System.Action<string> onFailed;
        private bool isActive;

        // 타임아웃이 났을 때 어느 단계에서 멈췄는지 알려 주기 위해 마지막으로 보고된 단계를 기억한다.
        private string lastStage = "최초 로딩 시작";

        /// <summary>최초 로딩 중이라 이 트래커가 로딩바를 숨길 책임을 지고 있으면 true.</summary>
        public bool IsActive => isActive;

        /// <param name="onFailed">타임아웃 등으로 최초 로딩이 끝내 끝나지 않았을 때 사유와 함께 호출된다. 호출 시점에는 아직 IsActive가 true이며,
        /// 받는 쪽이 Abandon()으로 로딩바를 정리해야 한다. null이면 로딩바만 숨긴다.</param>
        public InitialLoadingTracker(MonoBehaviour owner, System.Action<string> onFailed = null)
        {
            this.owner = owner;
            this.onFailed = onFailed;
        }

        private enum LoadingBarViewState
        {
            Missing,
            Hidden,
            Visible
        }

        /// <summary>
        /// 이전 씬에서 이어받은 로딩바가 떠 있으면 이 씬이 숨길 책임을 진다. 입장 응답(OnEntered)이 끝내 오지 않는 경우를 대비해 타임아웃을 건다.
        /// </summary>
        public void Begin()
        {
            LoadingBarViewState state = GameManager.Instance != null && GameManager.Instance.LoadingBarView != null
                ? (GameManager.Instance.LoadingBarView.gameObject.activeInHierarchy ? LoadingBarViewState.Visible : LoadingBarViewState.Hidden)
                : LoadingBarViewState.Missing;

            isActive = state == LoadingBarViewState.Visible;

            if (isActive)
            {
                _ = TimeoutAsync();
            }
        }

        /// <summary>최초 로딩 중일 때만 로딩바의 제목을 바꾼다. 진행률은 SceneLoadManager가 이미 100%로 채운 상태 그대로 둔다.</summary>
        public void Report(string title)
        {
            if (!isActive)
            {
                return;
            }

            lastStage = title;
            GameManager.Instance?.LoadingBarView?.UpdateTitle(title);
        }

        /// <summary>서버 입장이 확인되면 100%를 잠깐 보여준 뒤 로딩바를 숨긴다.</summary>
        public async Awaitable CompleteAsync()
        {
            if (!isActive)
            {
                return;
            }

            Report("입장 완료");
            await Awaitable.WaitForSecondsAsync(0.2f);

            if (owner == null)
            {
                return;
            }

            Hide();
        }

        /// <summary>이어받은 최초 로딩바를 숨긴다. 여러 번 불러도 한 번만 동작한다(맵 이동/재접속 로딩바와 섞이지 않는다).</summary>
        public void Hide()
        {
            if (!isActive)
            {
                return;
            }

            isActive = false;
            ClearAndHideLoadingBar();
        }

        /// <summary>
        /// 최초 로딩 여부와 무관하게 로딩바를 숨기고 추적을 끝낸다. 접속이 끝내 실패해 로비로 돌아갈 때 쓴다 -
        /// 재접속 안내 로딩바가 떠 있을 수 있어 Hide와 달리 항상 숨긴다.
        /// </summary>
        public void Abandon()
        {
            isActive = false;
            ClearAndHideLoadingBar();
        }

        private async Awaitable TimeoutAsync()
        {
            await Awaitable.WaitForSecondsAsync(TimeoutSeconds);

            if (owner == null || !isActive)
            {
                return;
            }

            string reason = $"게임 입장이 {TimeoutSeconds:0}초 안에 끝나지 않았습니다. (마지막 단계 : {lastStage})";
            DebugLogManager.GenerateErrorMessage<InitialLoadingTracker>(reason);

            // 로딩바만 숨기면 서버에 입장하지 못한 채 맵에 덩그러니 남는다 - 실패로 알리고 정리는 받는 쪽에 맡긴다.
            if (onFailed != null)
            {
                onFailed(reason);
                return;
            }

            Hide();
        }

        private static void ClearAndHideLoadingBar()
        {
            // LoadingBarView는 풀에서 재사용되는 인스턴스라 다음 사용처에 이 문구가 남지 않게 비운다.
            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);
            GameManager.Instance?.HideLoadingBar();
        }
    }
}
