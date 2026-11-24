using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    // Windows 서비스로 등록된 MariaDB를 켜고 끄는 역할. ManagedServer(dotnet run)와 달리 mysqld.exe를
    // 직접 자식 프로세스로 띄우지 않는다 - 이미 Windows 서비스(자동 시작)로 등록되어 있고, 서비스로 끄면
    // SCM이 정상 종료(flush)를 보장해준다. ManagedServer.Stop()처럼 taskkill /F로 강제 종료하면
    // DB가 비정상 종료되어 데이터가 손상될 수 있어 이 방식은 쓰지 않는다.
    // 서비스 시작/중지(sc start/stop)는 보통 관리자 권한이 필요하다 - Unity 에디터가 관리자 권한으로
    // 실행 중이 아니면 "액세스가 거부되었습니다" 류의 오류가 로그에 남는다(상태 조회는 권한 없이도 가능하다).
    internal sealed class ManagedWindowsService
    {
        private readonly string _displayName;
        private readonly string _serviceName;
        private readonly object _logLock = new();
        private readonly StringBuilder _log = new();
        private const int MaxLogChars = 20000;

        // 서비스 상태 조회(sc query)는 프로세스를 새로 띄우는 작업이라 OnGUI에서 매 프레임 부르면 느려진다.
        // 일정 간격으로만 다시 조회하고 그 사이에는 캐시된 값을 보여준다.
        private const double RecheckIntervalSeconds = 1.5;
        private string _cachedState = "확인 중...";
        private double _lastCheckedTime = -1;

        private volatile bool _isBusy;

        public ManagedWindowsService(string serviceName, string displayName)
        {
            _serviceName = serviceName;
            _displayName = displayName;
        }

        public string DisplayName => _displayName;
        public bool IsBusy => _isBusy;
        public bool IsRunning => CurrentState() == "RUNNING";

        public string StatusText
        {
            get
            {
                if (_isBusy) return "처리 중...";

                return CurrentState() switch
                {
                    "RUNNING" => "실행 중",
                    "STOPPED" => "중지됨",
                    "START_PENDING" => "시작 중...",
                    "STOP_PENDING" => "중지 중...",
                    "NOT_FOUND" => "서비스를 찾을 수 없음",
                    string s when s.StartsWith("오류") => s,
                    _ => "알 수 없음"
                };
            }
        }

        public Color StatusColor
        {
            get
            {
                if (_isBusy) return Color.yellow;

                return CurrentState() switch
                {
                    "RUNNING" => new Color(0.3f, 0.85f, 0.3f),
                    "NOT_FOUND" => Color.gray,
                    _ => new Color(0.9f, 0.3f, 0.3f)
                };
            }
        }

        // 마지막 조회로부터 RecheckIntervalSeconds가 지났을 때만 sc query를 다시 실행한다.
        private string CurrentState()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_lastCheckedTime < 0 || now - _lastCheckedTime >= RecheckIntervalSeconds)
            {
                _lastCheckedTime = now;
                _cachedState = QueryState();
            }
            return _cachedState;
        }

        private string QueryState()
        {
            try
            {
                var psi = new ProcessStartInfo("sc", $"query \"{_serviceName}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                if (output.Contains("RUNNING")) return "RUNNING";
                if (output.Contains("START_PENDING")) return "START_PENDING";
                if (output.Contains("STOP_PENDING")) return "STOP_PENDING";
                if (output.Contains("STOPPED")) return "STOPPED";
                return "NOT_FOUND";
            }
            catch (Exception e)
            {
                return $"오류: {e.Message}";
            }
        }

        // 다음 CurrentState() 호출에서 캐시를 쓰지 않고 바로 새로 조회하게 한다(Start/Stop 명령 종료 직후 사용).
        private void InvalidateStateCache()
        {
            _lastCheckedTime = -1;
        }

        public void Start() => RunScCommand("start", "시작");
        public void Stop() => RunScCommand("stop", "중지");

        private void RunScCommand(string _verb, string _actionLabel)
        {
            if (_isBusy) return;
            _isBusy = true;
            AppendLog($"[Editor] {_displayName} {_actionLabel} 요청.");

            var psi = new ProcessStartInfo("sc", $"{_verb} \"{_serviceName}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            process.Exited += (_, __) =>
            {
                int exitCode = process.ExitCode;
                process.Dispose();

                // Exited는 스레드풀 스레드에서 올라온다. 이후 처리가 _isBusy/로그 등 윈도우 상태를 바꾸므로 메인 스레드로 넘긴다.
                EditorApplication.delayCall += () =>
                {
                    _isBusy = false;
                    InvalidateStateCache();
                    AppendLog($"[Editor] {_actionLabel} 명령 종료. (ExitCode={exitCode})");
                };
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                _isBusy = false;
                AppendLog($"[Editor] {_actionLabel} 실패: {e.Message}");
            }
        }

        public string GetLogText()
        {
            lock (_logLock)
            {
                return _log.ToString();
            }
        }

        public void ClearLog()
        {
            lock (_logLock)
            {
                _log.Clear();
            }
        }

        private void AppendLog(string _line)
        {
            lock (_logLock)
            {
                _log.AppendLine(_line);
                if (_log.Length > MaxLogChars)
                {
                    _log.Remove(0, _log.Length - MaxLogChars);
                }
            }
        }
    }
}
