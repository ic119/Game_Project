using JJORY.Module;
using JJORY.Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JJORY.Module
{
    public interface Sequence
    {
        public IEnumerator Execute();
    }
}

public class SequenceActionUtils : SingletonObject<SequenceActionUtils>
{
    #region Variable
    private readonly Queue<Sequence> sequence_Queue = new Queue<Sequence>();
    private bool isRunnig = false;
    private bool isErrorOccurred = false;
    #endregion

    #region LifeCycle
    #endregion

    #region Method
    public void Enqueue(Sequence _sequence)
    {
        sequence_Queue.Enqueue(_sequence);
    }

    public void DoSequenceAction()
    {
        StartCoroutine(RunningExecute());
    }

    private IEnumerator RunningExecute()
    {
        isRunnig = true;
        while  (sequence_Queue.Count > 0)
        {
            if (isErrorOccurred == true)
            {
                Debug.LogError($">>>>> 이전 시퀀스에서 에러가 발생했습니다.");
                break;
            }
            Sequence cur_Sequence = sequence_Queue.Dequeue();
            yield return StartCoroutine(ExecuteWithCatch(cur_Sequence));
        }
        isRunnig = false;   
    }

    private IEnumerator ExecuteWithCatch(Sequence _sequence)
    {
        bool isFinished = false;

        IEnumerator e = _sequence.Execute();

        while(isFinished == false)
        {
            object cur_Sequence;
            
            try
            {
                if (e.MoveNext() == false)
                {
                    isFinished = true;
                    break;
                }

                cur_Sequence = e.Current;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($">>>>> Sequence Error) {ex.Message}\n{ex.StackTrace}");
                isErrorOccurred = true;
                yield break;
            }
            yield return cur_Sequence;
        }
    }
    #endregion
}