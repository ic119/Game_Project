using Incheol.Module;
using Incheol.Util;
using Incheol.View.UI;
using System;
using System.Collections.Generic;
using UnityEngine;

public class BootstrapController : MonoBehaviour
{
    #region Variable
    [Header("UI 참조")]
    [SerializeField] private UI_BootstrapSceneView bootstrapSceneView;

    [Header("Bootstrap 시점에 미리 Load할 Addressable Key 목록")]
    [SerializeField] private List<AddressKey> preloadAddressKey_List = new List<AddressKey>();

    [Header("모든 작업 완료 후 전환할 Scene 이름")]
    [SerializeField] private string loginSceneName = "LoginScene";

    /// <summary>
    /// Controller 생성 작업과 AddressableAsset Load 작업을 순서대로 담아두는 Queue.
    /// 각 작업은 Awaitable을 반환하는 함수 형태로 등록된다.
    /// </summary>
    private readonly Queue<Func<Awaitable>> task_Queue = new Queue<Func<Awaitable>>();
    #endregion

    #region LifeCycle
    private async void Awake()
    {
        EnqueueControllerTask();
        EnqueueAddressableLoadTask();

        await RunTaskQueueAsync();

        await LoadSceneController.Instance.LoadSceneAsync(loginSceneName);
    }
    #endregion

    #region Method
    /// <summary>
    /// SingletonObject 기반 Controller 생성 작업을 Queue에 추가한다.
    /// </summary>
    private void EnqueueControllerTask()
    {
        task_Queue.Enqueue(CreateSingletonAsync<AddressableAssetController>);
        task_Queue.Enqueue(CreateSingletonAsync<GameManager>);
        task_Queue.Enqueue(CreateSingletonAsync<EventController>);
        task_Queue.Enqueue(CreateSingletonAsync<LoadSceneController>);
    }

    /// <summary>
    /// preloadAddressKey_List에 등록된 AddressKey를 Load하는 작업을 Queue에 추가한다.
    /// </summary>
    private void EnqueueAddressableLoadTask()
    {
        foreach (AddressKey key in preloadAddressKey_List)
        {
            task_Queue.Enqueue(async () =>
            {
                await AddressableAssetController.Instance.LoadAsync<UnityEngine.Object>(key);
            });
        }
    }

    /// <summary>
    /// Queue에 담긴 작업을 하나씩 비동기로 꺼내 실행하고,
    /// 실행할 때마다 전체 대비 완료 개수를 기준으로 진행률을 갱신한다.
    /// </summary>
    private async Awaitable RunTaskQueueAsync()
    {
        int total = task_Queue.Count;
        int completed = 0;

        UpdateProgress(completed, total);

        while (task_Queue.Count > 0)
        {
            Func<Awaitable> task = task_Queue.Dequeue();

            try
            {
                await task.Invoke();
            }
            catch (Exception ex)
            {
                Utils.CreateLogError<BootstrapController>($"Task 실행 실패: {ex.Message}\n{ex.StackTrace}");
            }

            completed++;
            UpdateProgress(completed, total);
        }
    }

    /// <summary>
    /// completed / total 비율을 UI_BootstrapSceneView의 UI_ProgressBarView에 반영한다.
    /// </summary>
    private void UpdateProgress(int completed, int total)
    {
        if (bootstrapSceneView == null || total <= 0)
        {
            return;
        }

        float progress = (float)completed / total;
        bootstrapSceneView.UpdateProgress(progress);
    }

    /// <summary>
    /// SingletonObject&lt;T&gt; 기반 Controller를 생성(Instance 접근)한다.
    /// </summary>
    private static async Awaitable CreateSingletonAsync<T>() where T : SingletonObject<T>
    {
        _ = SingletonObject<T>.Instance;
        await Awaitable.NextFrameAsync();
    }
    #endregion
}
