-- 로컬 개발용 MariaDB 계정(game_server) + game_auth 데이터베이스를 생성하는 스크립트.
-- 운영(배포) 환경에는 절대 사용하지 말 것 - 이 계정/비밀번호는 로컬 개발 전용이며 appsettings.json과 동일한 값이다.
--
-- 사용법 (Windows, MariaDB가 로컬에 설치되어 있다고 가정):
--   root 권한으로 그대로 실행한다:
--     "C:\Program Files\MariaDB <버전>\bin\mysql.exe" -u root -p < setup-local-db.sql
--   appsettings.json에 이미 같은 비밀번호가 들어있으므로, 실행 후 별도 설정 없이 바로 dotnet run 하면 된다.

CREATE USER IF NOT EXISTS 'game_server'@'localhost' IDENTIFIED BY 'game_server_0000';
ALTER USER 'game_server'@'localhost' IDENTIFIED BY 'game_server_0000';

CREATE DATABASE IF NOT EXISTS game_auth;

GRANT ALL PRIVILEGES ON game_auth.* TO 'game_server'@'localhost';
FLUSH PRIVILEGES;
