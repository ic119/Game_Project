using System;
using System.IO;
using UnityEngine;

public class LogManager : MonoBehaviour
{
    #region Variable
    public string path;
    #endregion

    #region LifeCycle
    private void Awake()
    {
        Application.logMessageReceived += CreateAndWrite;
    }

    private void Start()
    {
        path = Path.Combine(Application.dataPath, "../Logs");
        Directory.CreateDirectory(path);
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= CreateAndWrite;
    }
    #endregion

    #region Method
    public string FileInfoCreate()
    {
        string name = path + "/Unity_Log_" + DateTime.Now.ToString("yyMMdd") + ".txt";
        //string namePath = name;
        return name;
    }

    public void CreateAndWrite(string _log, string _stackTrace, LogType _logType)
    {
        FileInfo fileInfo = new FileInfo(FileInfoCreate());

        if (Directory.Exists(path))
        {
            if (!fileInfo.Exists)   // 파일이 경로에 존재하지 않을 경우
            {
                using (StreamWriter writer = fileInfo.CreateText())
                {
                    switch (_logType)
                    {
                        case LogType.Log:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}]");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Assert:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Warning:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Exception:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Error:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                    }
                }
            }
            else                    // 파일이 경로에 존재하고 있을 겨우
            {
                using (StreamWriter writer = fileInfo.AppendText())
                {
                    switch (_logType)
                    {
                        case LogType.Log:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Assert:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Warning:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Exception:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                        case LogType.Error:
                            {
                                writer.WriteLine($"[{GetCurrentTime()}_{_logType}] ");
                                writer.WriteLine($"{_log}");
                                writer.WriteLine();
                                break;
                            }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Log를 남기는 현실 시간 Get
    /// </summary>
    /// <returns></returns>
    private string GetCurrentTime()
    {
        string time = null;
        time = DateTime.Now.ToString();
        return time;
    }
    #endregion
}
