using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Incheol.Editor
{
    // Server/MainServer 솔루션의 한 프로젝트(MainServer 또는 GameServer)를 "dotnet run"으로 띄우고 끄는 역할.
    // run-dev.ps1(각자 새 콘솔 창에서 dotnet run)과 같은 방식을 쓴다 - launchSettings.json(포트, ASPNETCORE_ENVIRONMENT)은
    // "dotnet run"일 때만 자동 적용되고, 빌드된 실행 파일(apphost)을 직접 실행하면 적용되지 않기 때문이다.
    internal sealed class ManagedServer
    {
        private readonly string _key;
        private readonly string _displayName;
        private readonly string _projectDirectory;

        private Process _process;
        private readonly object _logLock = new();
        private readonly StringBuilder _log = new();
        private const int MaxLogChars = 20000;

        private bool _isBuilding;
        private bool _lastRunFailed;

        public ManagedServer(string key, string displayName, string projectRelativePathFromAssets)
        {
            _key = key;
            _displayName = displayName;
            _projectDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, projectRelativePathFromAssets));
        }

        public string DisplayName => _displayName;
        public string ProjectDirectory => _projectDirectory;
        public bool IsBusy => _isBuilding;
        public bool IsRunning => _process != null && !_process.HasExited;

        public string StatusText
        {
            get
            {
                if (_isBuilding) return "빌드 중...";
                if (IsRunning) return $"실행 중 (PID {_process.Id})";
                if (_lastRunFailed) return "오류로 종료됨";
                return "중지됨";
            }
        }

        public Color StatusColor
        {
            get
            {
                if (_isBuilding) return Color.yellow;
                if (IsRunning) return new Color(0.3f, 0.85f, 0.3f);
                if (_lastRunFailed) return new Color(0.9f, 0.3f, 0.3f);
                return Color.gray;
            }
        }

        private string PidSessionKey => $"ServerLauncher.{_key}.Pid";

        // 도메인 리로드(스크립트 재컴파일)로 static 필드가 초기화되어도 실제 OS 프로세스는 계속 떠 있다.
        // SessionState는 도메인 리로드에도 남아 있으므로, PID로 다시 연결해 Stop이 여전히 동작하게 한다.
        // 단, 리로드 전에 걸어둔 로그 스트리밍(OutputDataReceived)은 다시 연결할 수 없어 이후 로그는 비어 있다.
        public void ReattachFromSession()
        {
            int pid = SessionState.GetInt(PidSessionKey, -1);
            if (pid <= 0) return;

            try
            {
                Process existing = Process.GetProcessById(pid);
                if (!existing.HasExited)
                {
                    _process = existing;
                    AppendLog("[Editor] 이전 세션에서 실행 중이던 프로세스에 다시 연결했습니다. (재컴파일 이후 로그는 여기 표시되지 않습니다)");
                }
                else
                {
                    SessionState.EraseInt(PidSessionKey);
                }
            }
            catch (ArgumentException)
            {
                SessionState.EraseInt(PidSessionKey);
            }
        }

        // 매 프레임 호출해 프로세스가 (외부 요인으로) 종료됐는지 확인한다. 상태가 바뀌었으면 true.
        public bool PollExited()
        {
            if (_process == null) return false;
            if (!_process.HasExited) return false;

            _lastRunFailed = _process.ExitCode != 0;
            AppendLog($"[Editor] 프로세스가 종료되었습니다. (ExitCode={_process.ExitCode})");
            _process = null;
            SessionState.EraseInt(PidSessionKey);
            return true;
        }

        public void Build()
        {
            if (_isBuilding || IsRunning) return;
            _isBuilding = true;
            AppendLog("[Editor] dotnet build 시작...");
            RunDotnet("build -c Debug", exitCode =>
            {
                _isBuilding = false;
                _lastRunFailed = exitCode != 0;
                AppendLog($"[Editor] 빌드 종료. (ExitCode={exitCode})");
            });
        }

        public void Start()
        {
            if (IsRunning || _isBuilding) return;

            var psi = new ProcessStartInfo("dotnet", "run")
            {
                WorkingDirectory = _projectDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                AppendLog($"[Editor] 실행 실패: {e.Message}");
                _lastRunFailed = true;
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            _lastRunFailed = false;
            SessionState.SetInt(PidSessionKey, process.Id);
            AppendLog($"[Editor] {_displayName} 시작. (PID {process.Id})");
        }

        public void Stop()
        {
            int pid = _process?.Id ?? SessionState.GetInt(PidSessionKey, -1);
            _process = null;
            SessionState.EraseInt(PidSessionKey);

            if (pid <= 0) return;

            AppendLog($"[Editor] {_displayName} 중지 요청. (PID {pid})");

            // "dotnet run"은 실제 서버를 자식 프로세스로 한 번 더 띄울 수 있다 - 부모만 죽이면 서버가 고아로 남으므로
            // taskkill /T로 프로세스 트리 전체를 끈다. (Process.Kill(true)는 Unity 런타임에서 동작을 보장할 수 없어 쓰지 않는다)
            try
            {
                Process killer = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                killer?.WaitForExit(5000);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerLauncher] PID {pid} 종료 중 오류: {e.Message}");
            }
        }

        private void RunDotnet(string arguments, Action<int> onExit)
        {
            var psi = new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = _projectDirectory,
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
                int code = process.ExitCode;
                process.Dispose();
                // Exited는 스레드풀 스레드에서 올라온다. onExit이 윈도우 상태를 바꾸므로 메인 스레드로 넘긴다.
                EditorApplication.delayCall += () => onExit(code);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void AppendLog(string line)
        {
            lock (_logLock)
            {
                _log.AppendLine(line);
                if (_log.Length > MaxLogChars)
                {
                    _log.Remove(0, _log.Length - MaxLogChars);
                }
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
    }
}
