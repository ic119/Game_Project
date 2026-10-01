using System;
using System.Collections.Generic;
using Incheol.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI_GameSceneView에서 분리된 채팅 UI. 입력/스크롤/메시지 인스턴스 관리만 담당하고,
/// 실제 서버 전송/수신 라우팅은 Presenter(GameSceneManager)가 MessageSubmitted 이벤트와
/// AddChatMessage 호출을 통해 처리한다.
/// </summary>
public class UI_ChatView : MonoBehaviour
{
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private RectTransform chatContentRoot;
    [SerializeField] private GameObject chatMessageTemplate;

    /// <summary>
    /// 채팅창에 쌓아두는 메시지 아이템의 최대 개수. 세션이 길어져도 UI 오브젝트가 무한히 늘어나지 않도록
    /// 오래된 메시지부터 제거한다.
    /// </summary>
    private const int MaxChatMessageCount = 100;

    private readonly Queue<GameObject> chatMessageInstances = new();

    public event Action<string> MessageSubmitted;

    // 입력창이 포커스된 상태를 마지막으로 InputBlocker에 알린 값. 바뀔 때만 다시 알린다.
    private bool isBlockingGameplayInput;

    private void OnEnable()
    {
        if (chatInputField != null)
        {
            chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        }
    }

    private void OnDisable()
    {
        if (chatInputField != null)
        {
            chatInputField.onSubmit.RemoveListener(OnChatInputSubmit);
        }

        // 입력창에 포커스가 있는 채로 이 뷰가 꺼지거나 파괴되면(맵 전환, 로그아웃 등) 입력이 영원히 막히므로 반드시 푼다.
        SetGameplayInputBlocked(false);
    }

    /// <summary>
    /// 채팅 입력창에 글자를 치는 동안(포커스 상태)에는 채팅 입력으로 이동/대쉬/공격/단축키/상호작용이 호출되지 않도록
    /// InputBlocker로 게임플레이 입력을 막는다. 이벤트(onSelect/onDeselect) 대신 isFocused를 매 프레임 확인한다 -
    /// Esc로 입력을 취소하면 입력창은 선택된 채로 입력만 끝나 onDeselect가 오지 않기 때문에, 상태를 직접 읽어야 놓치지 않는다.
    /// </summary>
    private void Update()
    {
        SetGameplayInputBlocked(chatInputField != null && chatInputField.isFocused);
    }

    private void SetGameplayInputBlocked(bool blocked)
    {
        if (isBlockingGameplayInput == blocked)
        {
            return;
        }

        isBlockingGameplayInput = blocked;
        InputBlocker.SetBlocked(this, blocked);
    }

    /// <summary>
    /// ChatContainer/Scroll View/Viewport/Content 아래에 chatMessageTemplate을 복제해 한 줄을 추가한다.
    /// MaxChatMessageCount를 넘으면 가장 오래된 항목부터 제거하고, 추가 직후 스크롤을 맨 아래로 내린다.
    /// 리치 텍스트는 채팅 내용에 꺾쇠 문자가 섞여 들어와도 태그로 해석되지 않도록 항상 꺼둔다.
    /// </summary>
    public void AddChatMessage(string _nickname, string _message)
    {
        if (chatMessageTemplate == null || chatContentRoot == null)
        {
            return;
        }

        GameObject instance = Instantiate(chatMessageTemplate, chatContentRoot);
        instance.SetActive(true);

        if (instance.TryGetComponent(out TextMeshProUGUI text))
        {
            text.richText = false;
            text.text = string.IsNullOrEmpty(_nickname) ? _message : $"[{_nickname}]: {_message}";
        }

        chatMessageInstances.Enqueue(instance);

        while (chatMessageInstances.Count > MaxChatMessageCount)
        {
            GameObject oldest = chatMessageInstances.Dequeue();
            if (oldest != null)
            {
                Destroy(oldest);
            }
        }

        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    /// <summary>
    /// ChatInputField에서 Enter(Submit)를 누르면 호출된다. 빈 문자열은 무시하고,
    /// 전송 후에는 입력창을 비우고 즉시 재포커스해 연속으로 대화를 이어갈 수 있게 한다.
    /// </summary>
    private void OnChatInputSubmit(string _text)
    {
        if (string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        MessageSubmitted?.Invoke(_text.Trim());

        chatInputField.text = string.Empty;
        chatInputField.ActivateInputField();
    }
}
