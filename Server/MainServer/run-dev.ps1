# MainServer(AuthServer/CharacterServer, HTTP)와 GameServer(TCP 9000)를 로컬 개발용으로 동시에 실행한다.
# 두 프로세스를 각각 별도 콘솔 창으로 띄우므로 로그를 따로 확인할 수 있다.
# 종료하려면 각 창을 닫거나 Ctrl+C를 누르면 된다.

$root = $PSScriptRoot

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\MainServer'; dotnet run"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\GameServer'; dotnet run"

Write-Host "MainServer(HTTP)와 GameServer(TCP 9000)를 각각 새 창에서 실행했습니다."
