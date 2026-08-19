# MainServer

게임 클라이언트를 위한 **인증(Auth) 서버**. 회원가입, 로그인, JWT 액세스/리프레시 토큰 발급 및 재발급, 로그아웃 기능을 제공하는 ASP.NET Core Web API 프로젝트입니다.

## 기술 스택

| 구분 | 내용 |
|---|---|
| 프레임워크 | ASP.NET Core 8.0 (Web API, `Microsoft.NET.Sdk.Web`) |
| ORM | Entity Framework Core 9.0.0 |
| DB | MySQL 호환 (Pomelo.EntityFrameworkCore.MySql 9.0.0) — 로컬 개발은 MariaDB로 검증됨 |
| 인증 | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`) |
| 비밀번호 해싱 | BCrypt.Net-Next |

## 폴더 구조

```
MainServer/                                솔루션 루트
├── MainServer.sln
└── MainServer/                            프로젝트 루트
    ├── MainServer.csproj
    ├── Program.cs                         진입점 — DI/미들웨어 구성
    ├── appsettings.json                   DB 연결문자열, JWT 설정
    ├── Properties/
    │   └── launchSettings.json            로컬 실행 프로필/URL
    ├── Migrations/                        EF Core 마이그레이션 이력 (자동 생성)
    │   ├── 20260819022036_InitialCreate.cs
    │   ├── 20260819022036_InitialCreate.Designer.cs
    │   └── AppDbContextModelSnapshot.cs
    └── AuthServer/                        인증 도메인 전용 코드
        ├── Entities/                      DB 테이블과 매핑되는 도메인 모델
        │   ├── User.cs
        │   └── RefreshToken.cs
        ├── Data/
        │   └── AppDbContext.cs            EF Core DbContext
        ├── DTOs/                          컨트롤러 ↔ 클라이언트 요청/응답 모델
        │   ├── RegisterRequest.cs
        │   ├── LoginRequest.cs
        │   ├── RefreshRequest.cs
        │   ├── UserResponse.cs
        │   └── LoginResponse.cs
        ├── Services/                      비즈니스 로직 (DB 접근은 여기서만)
        │   ├── IUserService.cs / UserService.cs
        │   └── IAuthService.cs / AuthService.cs
        ├── Helpers/
        │   └── JWTHelper.cs               Access/Refresh 토큰 생성 유틸
        └── Controllers/                   HTTP 엔드포인트
            ├── UserController.cs          /api/users/*
            └── AuthController.cs          /api/auth/*
```

전형적인 **Controller → Service → DbContext(EF Core) → MySQL** 3계층 구조를 따릅니다.

## 도메인 모델

### `User` (테이블: `users`)
| 필드 | 타입 | 비고 |
|---|---|---|
| Id | long | PK, auto-increment |
| Username | string | 유니크 인덱스 |
| PasswordHash | string | BCrypt 해시 저장 (평문 저장 안 함) |
| Nickname | string | |
| CreatedAt / UpdatedAt | DateTime | |
| IsActive | bool | 기본값 true |

### `RefreshToken` (테이블: `refresh_tokens`)
| 필드 | 타입 | 비고 |
|---|---|---|
| Id | long | PK |
| UserId | long | FK → `users.Id`, `ON DELETE CASCADE` |
| Token | string | 유니크 인덱스, Base64(64바이트 랜덤값) |
| ExpiresAt | DateTime | 발급 시점 +14일 |
| IsRevoked | bool | 로그아웃/재발급 시 true로 마킹 |
| CreatedAt | DateTime | |

`AppDbContext.OnModelCreating`에서 테이블명 매핑, 유니크 인덱스, FK cascade 규칙을 정의합니다.

## API 엔드포인트

### `UserController` — `/api/users`

| 메서드 | 경로 | 설명 | 성공 | 실패 |
|---|---|---|---|---|
| POST | `/register` | 회원가입 (BCrypt 해싱 후 저장) | 200 `UserResponse` | 409 아이디 중복 |
| GET | `/{id}` | ID로 사용자 조회 | 200 `UserResponse` | 404 없음 |

### `AuthController` — `/api/auth`

| 메서드 | 경로 | 설명 | 성공 | 실패 |
|---|---|---|---|---|
| POST | `/login` | 아이디/비밀번호 검증 후 토큰 발급 | 200 `LoginResponse` | 401 아이디/비밀번호 불일치 |
| POST | `/refresh` | refresh token으로 토큰 재발급(로테이션: 기존 토큰 즉시 revoke) | 200 `LoginResponse` | 401 유효하지 않거나 만료된 토큰 |
| POST | `/logout` | refresh token을 revoke 처리 | 204 | - |

## 요청 흐름

**회원가입**
`UserController.Register` → `UserService.RegisterAsync` → 아이디 중복 체크 → 중복이면 예외 → 컨트롤러가 409로 변환 → BCrypt 해싱 후 `User` 저장 → `UserResponse` 반환

**로그인**
`AuthController.Login` → `AuthService.LoginAsync` → `Username`으로 조회 + `BCrypt.Verify` → 실패 시 401 → 성공 시:
1. `JWTHelper.GenerateAccessToken` — 30분 만료 JWT (`sub`=userId, `unique_name`=username)
2. `JWTHelper.GenerateRefreshToken` — 64바이트 랜덤값(Base64)
3. `RefreshToken` 레코드 DB 저장 (14일 만료)
4. `LoginResponse` 반환

**토큰 재발급**
`AuthController.Refresh` → `AuthService.RefreshAsync` → 제출된 토큰 조회 → 없음/revoke됨/만료면 401 → 유효하면 기존 토큰 revoke 후 새 access/refresh 토큰 발급(토큰 로테이션)

**로그아웃**
`AuthController.Logout` → `AuthService.LogoutAsync` → 해당 refresh token을 `IsRevoked = true`로 마킹

## 설정 (`appsettings.json`)

```json
{
  "ConnectionStrings": { "Default": "Server=...;Database=game_auth;User=game_server;Password=..." },
  "Jwt": { "Key": "...", "Issuer": "GameAuthServer" }
}
```

- `ConnectionStrings:Default` — MySQL/MariaDB 연결 문자열
- `Jwt:Key` — JWT 서명용 대칭키 (32자 이상 랜덤 문자열)
- `Jwt:Issuer` — JWT `iss` 클레임 값

> **보안 유의사항**: DB 비밀번호와 JWT 서명 키가 평문으로 들어 있습니다. 저장소에 커밋하거나 배포할 경우 `.gitignore` 처리 또는 User Secrets/환경 변수로 분리하는 것을 권장합니다.

## 빌드 & 실행

```powershell
# 솔루션 루트에서
dotnet build

# 프로젝트 루트(MainServer/MainServer)에서 실행
dotnet run
```

`Properties/launchSettings.json`에 정의된 URL로 기동됩니다: `https://localhost:58208`, `http://localhost:58209` (HTTP 요청은 `UseHttpsRedirection`에 의해 자동으로 HTTPS로 리다이렉트됨).

로컬 HTTPS 인증서를 신뢰하지 않은 상태라면:
```powershell
dotnet dev-certs https --trust
```

## DB 마이그레이션

```powershell
dotnet tool install --global dotnet-ef   # 최초 1회
dotnet ef migrations add <이름>          # 스키마 변경 시
dotnet ef database update                # 실제 DB에 반영
```

`Migrations/` 폴더에 마이그레이션 이력이 코드로 남으며, `InitialCreate` 마이그레이션으로 `users`/`refresh_tokens` 테이블이 이미 생성되어 있습니다.

## 현재 상태 / 알려진 사항

- 빌드/실행/전체 API 흐름(회원가입 → 로그인 → 재발급 → 로그아웃, 각종 에러 케이스 포함)이 실제 DB 연동까지 정상 동작 확인됨.
- JWT 인증 스킴은 등록되어 있으나, 아직 `[Authorize]`가 적용된 엔드포인트는 없음 — 인증이 필요한 API를 추가할 때 적용 필요.
- 서버를 상시 구동하려면 별도의 자동 실행 설정(Windows 서비스 등록 또는 작업 스케줄러)이 필요하며, 아직 구성되어 있지 않음.
- DTO(`RegisterRequest`, `LoginRequest` 등)의 레코드 프로퍼티는 `_username`처럼 언더스코어 프리픽스 네이밍을 프로젝트 전반에서 일관되게 사용함.
