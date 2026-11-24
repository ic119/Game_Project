using UnityEditor;
using UnityEngine;

namespace Incheol.Editor
{
    // MainServer(Auth/Character, HTTP)와 GameServer(TCP 9000)를 에디터에서 바로 켜고 끄는 도구.
    // 터미널을 따로 열 필요 없이 Unity 안에서 로그를 보면서 서버를 재시작할 수 있게 한다.
    // 로컬 MySQL(3306)은 이 도구가 관리하지 않으므로 MainServer를 켜기 전에 따로 켜 둬야 한다.
    public class ServerLauncherWindow : EditorWindow
    {
        private static readonly ManagedServer[] s_servers =
        {
            new ManagedServer("MainServer", "MainServer (Auth / Character, HTTP)", "../../Server/MainServer/MainServer"),
            new ManagedServer("GameServer", "GameServer (TCP 9000)", "../../Server/MainServer/GameServer"),
        };

        // MainServer가 의존하는 로컬 DB. dotnet run으로 띄우는 위 서버들과 달리 Windows 서비스를 start/stop한다
        // (ManagedWindowsService 참고) - 서비스 이름이 다르면 생성자 첫 번째 인자만 바꾸면 된다.
        private static readonly ManagedWindowsService s_database = new ManagedWindowsService("MariaDB", "MariaDB (로컬 DB)");

        private readonly bool[] _logFoldouts = new bool[s_servers.Length];
        private readonly Vector2[] _logScroll = new Vector2[s_servers.Length];
        private bool _dbLogFoldout;
        private Vector2 _dbLogScroll;

        [MenuItem("Tools/Server/Server Launcher")]
        private static void Open()
        {
            GetWindow<ServerLauncherWindow>("Server Launcher").Show();
        }

        [MenuItem("Tools/Server/Start All")]
        private static void StartAll()
        {
            foreach (ManagedServer server in s_servers) server.Start();
        }

        [MenuItem("Tools/Server/Start Database")]
        private static void StartDatabase()
        {
            s_database.Start();
        }

        [MenuItem("Tools/Server/Stop Database")]
        private static void StopDatabase()
        {
            s_database.Stop();
        }

        [MenuItem("Tools/Server/Stop All")]
        private static void StopAll()
        {
            foreach (ManagedServer server in s_servers) server.Stop();
        }

        // 도메인 리로드(스크립트 재컴파일) 때마다 호출된다 - 이전에 띄워둔 서버 프로세스를 PID로 다시 연결하고,
        // 에디터를 완전히 끌 때 서버 프로세스가 고아로 남지 않도록 종료 훅을 걸어둔다.
        [InitializeOnLoadMethod]
        private static void Init()
        {
            foreach (ManagedServer server in s_servers) server.ReattachFromSession();

            EditorApplication.quitting -= StopAll;
            EditorApplication.quitting += StopAll;
        }

        private void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        // DB는 PID로 상태를 추적하지 않고 sc query로 조회하므로(ManagedWindowsService 참고), 백그라운드에서
        // 바뀐 상태가 창에 보이도록 1초 간격으로 다시 그린다.
        private double _nextDatabaseRepaintTime;

        private void OnEditorUpdate()
        {
            bool changed = false;
            foreach (ManagedServer server in s_servers)
            {
                if (server.PollExited()) changed = true;
            }

            if (EditorApplication.timeSinceStartup >= _nextDatabaseRepaintTime)
            {
                _nextDatabaseRepaintTime = EditorApplication.timeSinceStartup + 1.0;
                changed = true;
            }

            if (changed) Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("MainServer는 로컬 DB(MariaDB)가 켜져 있어야 정상적으로 뜹니다. 아래에서 먼저 켜 두세요.", MessageType.Info);
            EditorGUILayout.Space(4);

            DrawDatabase();
            EditorGUILayout.Space(10);

            for (int i = 0; i < s_servers.Length; i++)
            {
                DrawServer(s_servers[i], i);
                EditorGUILayout.Space(10);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("전체 시작")) StartAll();
                if (GUILayout.Button("전체 중지")) StopAll();
            }
        }

        // DB는 다른 프로그램도 같이 쓸 수 있는 공용 서비스라 StartAll/StopAll(에디터 종료 시 포함)에는 묶지 않는다 -
        // MainServer/GameServer를 전체 중지한다고 DB까지 같이 내려가면 당황스러울 수 있다. 이 패널에서 따로 켜고 끈다.
        private void DrawDatabase()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(s_database.DisplayName, EditorStyles.boldLabel);

                Color prevColor = GUI.contentColor;
                GUI.contentColor = s_database.StatusColor;
                EditorGUILayout.LabelField($"● {s_database.StatusText}");
                GUI.contentColor = prevColor;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(s_database.IsRunning || s_database.IsBusy))
                    {
                        if (GUILayout.Button("Start")) s_database.Start();
                    }
                    using (new EditorGUI.DisabledScope(!s_database.IsRunning || s_database.IsBusy))
                    {
                        if (GUILayout.Button("Stop")) s_database.Stop();
                    }
                    if (GUILayout.Button("로그 지우기")) s_database.ClearLog();
                }

                _dbLogFoldout = EditorGUILayout.Foldout(_dbLogFoldout, "로그");
                if (_dbLogFoldout)
                {
                    _dbLogScroll = EditorGUILayout.BeginScrollView(_dbLogScroll, GUILayout.Height(100));
                    EditorGUILayout.TextArea(s_database.GetLogText(), GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawServer(ManagedServer server, int index)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(server.DisplayName, EditorStyles.boldLabel);

                Color prevColor = GUI.contentColor;
                GUI.contentColor = server.StatusColor;
                EditorGUILayout.LabelField($"● {server.StatusText}");
                GUI.contentColor = prevColor;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(server.IsBusy || server.IsRunning))
                    {
                        if (GUILayout.Button("Build")) server.Build();
                    }
                    using (new EditorGUI.DisabledScope(server.IsRunning || server.IsBusy))
                    {
                        if (GUILayout.Button("Start")) server.Start();
                    }
                    using (new EditorGUI.DisabledScope(!server.IsRunning))
                    {
                        if (GUILayout.Button("Stop")) server.Stop();
                    }
                    if (GUILayout.Button("폴더 열기")) EditorUtility.RevealInFinder(server.ProjectDirectory);
                    if (GUILayout.Button("로그 지우기")) server.ClearLog();
                }

                _logFoldouts[index] = EditorGUILayout.Foldout(_logFoldouts[index], "로그");
                if (_logFoldouts[index])
                {
                    _logScroll[index] = EditorGUILayout.BeginScrollView(_logScroll[index], GUILayout.Height(160));
                    EditorGUILayout.TextArea(server.GetLogText(), GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
