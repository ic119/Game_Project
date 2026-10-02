using Incheol.Controller;
using Incheol.Models.Define;
using Incheol.Modules;
using Incheol.Modules.Networking;
using Incheol.Utils;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Incheol.Presenter.Scene
{
    // 게임 서버 연결 상태(오류/중복 접속/재접속/끊김)와 로그아웃 처리.
    public partial class GameSceneManager
    {
        #region Method
        /// <summary>
        /// GameServer가 Game_EnterRequest 인증 실패 등으로 연결을 끊기 직전에 보낸 사유(System_Error)를 알림 팝업으로 보여준다.
        /// </summary>
        private void HandleGameServerError(string message)
        {
            GameManager.Instance?.ShowAlarmPopup("서버 오류", message);
        }

        /// <summary>
        /// 서버가 이 접속을 강제로 끊었을 때(Game System_Kicked, 같은 캐릭터로 다른 곳에서 접속 등) 호출된다.
        /// 게임 서버 연결은 이미 끊겼으므로 조작을 막고, UI_GameSceneView에 연결된 전용 팝업(UI_SessionKickedPopupView)으로
        /// 사유를 보여준 뒤 확인을 누르면 로그아웃해 로그인 화면으로 돌아간다. 이 기기의 refresh token만 폐기하므로
        /// 새로 접속한 쪽의 로그인에는 영향이 없다. 팝업이 연결돼 있지 않으면 공용 알림 팝업으로 알리고 바로 돌아간다.
        /// 마지막 접속시간(TouchLastLogin)은 기록하지 않는다 - 캐릭터는 다른 곳에서 계속 접속 중이기 때문이다.
        /// </summary>
        private void HandleSessionKicked(string reason)
        {
            SetLocalPlayerControlEnabled(false);

            UI_SessionKickedPopupView popup = gameSceneView != null ? gameSceneView.SessionKickedPopup : null;
            if (popup != null)
            {
                popup.Show(reason, LogoutWithoutTouchingLastLogin);
                return;
            }

            GameManager.Instance?.ShowAlarmPopup("접속 종료", reason);
            LogoutWithoutTouchingLastLogin();
        }

        private void LogoutWithoutTouchingLastLogin()
        {
            if (isLoggingOut || ServerConnectManager.Instance == null)
            {
                return;
            }

            isLoggingOut = true;
            PerformLogout();
        }

        /// <summary>
        /// GameServer 입장(최초/재접속)이 받아들여졌을 때 서버가 정한 내 위치/체력으로 맞춘다. 최초 입장은 클라이언트도 같은
        /// RespawnPoint에 가득 찬 체력으로 시작해 사실상 변화가 없고, 재접속은 끊기기 전과 달라진 위치/체력을 여기서 맞춘다.
        /// </summary>
        private void HandleGameServerEntered(GamePlayerInfo self)
        {
            if (spawnedPlayerModel == null || !IsLocalPlayer(self.PlayerId))
            {
                return;
            }

            WarpLocalPlayer(new Vector3(self.X, self.Y, self.Z), Quaternion.Euler(0f, self.RotationY, 0f));
            ApplyLocalServerHp(self.CurrentHp, self.MaxHp, false);
        }

        /// <summary>
        /// GameServer 연결이 예기치 않게 끊겨 자동 재접속을 시도할 때마다 호출된다. 서버와 주고받을 수 없는 동안 조작을 막고
        /// 로딩바로 안내한다. 화면의 원격 플레이어/몬스터는 끊긴 시점 그대로라 믿을 수 없으므로 첫 시도에서 비운다 -
        /// 재접속하면 Game_EnterAck이 시야 안의 목록으로 다시 채운다.
        /// </summary>
        private void HandleGameServerReconnecting(int attempt, int maxAttempts)
        {
            if (attempt == 1)
            {
                // 응답을 받을 수 없게 됐으므로 대기 상태를 풀어준다. 서버의 물약 대기시간은 세션마다 하나라 재접속하면
                // 초기화되므로 이쪽 표시도 함께 비운다.
                isUseItemPending = false;
                potionReadyAtTime = 0f;
                inventoryView?.SetPotionCooldown(0f);

                SetLocalPlayerControlEnabled(false);
                RemotePlayerManager.Instance?.ClearAll();
                RemoteMonsterManager.Instance?.ClearAll();
                RemoteChestManager.Instance?.ClearAll();
                RemoteGateManager.Instance?.ClearAll();
                monsterTargetView?.ClearTarget();
                GameManager.Instance?.ShowLoadingBar();
            }

            GameManager.Instance?.LoadingBarView?.UpdateTitle($"게임 서버에 다시 연결하는 중... ({attempt}/{maxAttempts})");
        }

        /// <summary>
        /// 자동 재접속에 성공했을 때 호출된다. 위치/체력은 바로 앞의 HandleGameServerEntered가 이미 맞췄다.
        /// </summary>
        private void HandleGameServerReconnected()
        {
            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);
            GameManager.Instance?.HideLoadingBar();

            if (spawnedPlayerModel != null)
            {
                SetLocalPlayerControlEnabled(!spawnedPlayerModel.IsDead);
            }

            chatView?.AddChatMessage("시스템", "게임 서버에 다시 연결되었습니다.");
        }

        /// <summary>
        /// GameServer 연결이 예기치 않게 끊기고 자동 재접속도 모두 실패했을 때(또는 입장 자체를 하지 못했을 때) 호출된다
        /// (씬 전환 등으로 직접 Disconnect()를 호출한 경우는 포함되지 않음). 서버 없이는 진행할 수 없으므로 알린 뒤
        /// 로비로 돌아간다 - 로비에서 다시 시작하면 새로 입장한다.
        /// </summary>
        private void HandleGameServerDisconnected()
        {
            // 응답을 받을 수 없게 됐으므로 대기 상태를 풀어준다.
            isUseItemPending = false;
            SetLocalPlayerControlEnabled(false);

            GameManager.Instance?.LoadingBarView?.UpdateTitle(string.Empty);
            GameManager.Instance?.HideLoadingBar();
            GameManager.Instance?.ShowAlarmPopup("연결 끊김", "게임 서버에 연결할 수 없어 로비로 돌아갑니다.");

            if (SceneLoadManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SceneLoadManager.Instance가 null입니다.");
                return;
            }

            SceneLoadManager.Instance.LoadSceneByTags("LobbyScene");
        }

        /// <summary>
        /// logoutButton 클릭 시 호출된다. AccessToken이 아직 살아있는 동안 선택된 캐릭터의 마지막 접속시간부터
        /// 기록한 뒤(SaveDataManager.TouchLastLogin), 계정 세션(ServerConnectManager, Access/RefreshToken)을
        /// 종료하고 완료되면 LoginScene으로 전환한다. 순서를 바꿔 Logout을 먼저 하면 AccessToken이 지워져
        /// TouchLastLogin 요청이 401로 실패한다. GameServer(TCP) 연결은 씬 전환으로 GameScene이 언로드될 때
        /// OnDestroy에서 자동으로 끊기므로 여기서 별도로 처리하지 않는다.
        /// </summary>
        private void HandleLogoutButtonClicked()
        {
            if (isLoggingOut)
            {
                return;
            }

            if (ServerConnectManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("ServerConnectManager.Instance가 null입니다.");
                return;
            }

            isLoggingOut = true;

            if (SaveDataManager.Instance != null && SaveDataManager.Instance.SelectedCharacterId.HasValue)
            {
                SaveDataManager.Instance.TouchLastLogin(_ => PerformLogout());
            }
            else
            {
                PerformLogout();
            }
        }

        private void PerformLogout()
        {
            ServerConnectManager.Instance.Logout(_ => TransitionToLoginScene());
        }

        private void TransitionToLoginScene()
        {
            isLoggingOut = false;

            if (SceneLoadManager.Instance == null)
            {
                DebugLogManager.GenerateErrorMessage<GameSceneManager>("SceneLoadManager.Instance가 null입니다.");
                return;
            }

            SceneLoadManager.Instance.LoadSceneByTags("LoginScene");
        }

        #endregion
    }
}
