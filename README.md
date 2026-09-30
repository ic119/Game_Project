# Game_Project

Unity 클라이언트와 .NET 서버(MainServer + GameServer)로 구성된 멀티플레이 RPG 프로젝트입니다.
**서버 권위(server-authoritative)** 구조로, 클라이언트는 요청만 보내고 판정과 결과는 서버가 결정합니다.

- **Client**: Unity 6 (6000.4.8f1), URP, Addressables, TextMeshPro, DOTween
- **Server**: .NET 8 (ASP.NET Core, TCP), MySQL

## 목차
1. [전체 구조](#1-전체-구조)
2. [저장소 구성](#2-저장소-구성)
3. [게임 진행 흐름](#3-게임-진행-흐름)
4. [핵심 기능](#4-핵심-기능)
5. [클라이언트 구조](#5-클라이언트-구조)
6. [데이터 원본과 흐름](#6-데이터-원본과-흐름)
7. [실행 방법](#7-실행-방법)
8. [관련 문서](#8-관련-문서)

---

## 1. 전체 구조

```
              HTTPS (JWT)                          TCP 9000 (바이너리 패킷)
 ┌────────────────────────────┐             ┌────────────────────────────────┐
 │ MainServer (ASP.NET)        │             │ GameServer (실시간 월드)        │
 │  · 계정/인증 (AuthController)│             │  · GameRoom (맵별 50ms 틱)      │
 │  · 캐릭터/인벤토리/장비 (DB)  │◄────────────│  · 몬스터 AI, 전투, 드랍, 상자   │
 │  · MySQL (game_auth)         │  내부 API    │  · ClientSession (접속 1개)     │
 └──────────▲─────────────────┘ (InternalApi)└──────────▲─────────────────────┘
            │ REST                                       │
            └───────────────┐        ┌──────────────────┘
                     ┌──────┴────────┴──────┐
                     │     Unity Client      │
                     └───────────────────────┘
```

| 구성 | 역할 | 데이터 성격 |
|---|---|---|
| **MainServer** | 계정, 로그인(JWT), 캐릭터 생성·삭제, 인벤토리, 장비 장착, 킬 보상 저장 | 영속 데이터의 유일한 주인 (DB) |
| **GameServer** | 실시간 이동·전투·몬스터·상자·채팅 중계 | 휘발성 상태(메모리), 판정 권한 |
| **Shared** | 패킷 정의, OpCode, `BinaryPacket` 직렬화 | 두 서버가 같은 프로토콜 사용 |
| **Client** | 렌더링, 입력, 서버 결과 표시 | 서버가 알려준 것만 표시 |

## 2. 저장소 구성

```
Game_Project/
├─ Client/                          Unity 프로젝트
│  └─ Assets/@Scripts/
│     ├─ Controller/                입력, 이동, 공격, 상호작용, 카메라
│     ├─ Model/                     PlayerCharacterModel, EquipmentController, Health/CombatStat
│     ├─ Models/                    ItemData, UserSaveData, Define(enum), ScriptableObject
│     ├─ View/UI/                   인벤토리, 로비, 캐릭터 프리뷰 등 화면
│     ├─ Presenter/Scene/           GameSceneManager, LobbySceneManager 등 씬 흐름
│     ├─ Modules/                   싱글턴 매니저(REST/TCP 접속, DB 로딩, 원격 개체 관리)
│     └─ Editor/                    맵 데이터 내보내기, 아이템 정의 생성 도구
└─ Server/
   ├─ MainServer/
   │  ├─ MainServer/                ASP.NET (AuthServer, CharacterServer)
   │  ├─ GameServer/                TCP 실시간 서버
   │  ├─ Shared/                    패킷/OpCode 공용 라이브러리
   │  └─ GameServer.Tests/          GameServer 단위 테스트
   └─ *.txt                         밸런싱·드랍 설정·배포 가이드
```

## 3. 게임 진행 흐름

### ① 로그인 → 캐릭터 선택 (HTTP, MainServer)
```
Bootstrap 씬  ── ItemDatabaseSO / 몬스터 DB 등 Addressables 로드
Login         ── POST /login → JWT(AccessToken) 발급
Lobby         ── GET 캐릭터 목록 → 선택 → GET /{id} 상세(레벨·스탯·아이템+equipSlot)
                 └ 프리뷰 모델에 헤어/눈/입 + 저장된 장착 장비 적용
                 └ 생성/삭제/외형 수정은 POST/DELETE/PUT
```

### ② 게임 씬 진입 (HTTP → TCP 전환)
```
GameSceneManager: 캐릭터 스폰 → 체력/스탯/골드 적용 → 장착 아이템 복원 + 장비 보너스 계산
  → Game_EnterRequest(AccessToken, characterId, 능력치, 외형)
GameServer: PlayerAuthValidator가 MainServer에 토큰·소유권 확인을 위임
            공격력/방어력은 클라이언트 자기 보고가 아니라 DB의 장착 아이템에서 재계산(위조 방지)
  ── Game_EnterAck: 기존 플레이어 / 몬스터 / 활성 상자 목록 ──►
```
GameServer는 JWT 서명 키나 DB에 직접 접근하지 않고 MainServer에 검증을 맡깁니다. 두 서버가 키를 이중으로 들고 어긋나는 문제를 피하기 위한 설계입니다.

### ③ 실시간 루프 (TCP, GameServer)
- `GameRoom`(맵마다 1개)이 **50ms 틱**으로 몬스터 AI(추적·공격)를 돌리고, 위치를 `Game_WorldSnapshot`으로 묶어 방송합니다.
- **시야 기반 방송**: 근처 몬스터/플레이어만 전달하고, 벗어나면 `Game_MonsterLeaveView`로 정리합니다.
- 클라이언트는 `GameServerConnectManager` 이벤트를 받아 `RemotePlayerManager` / `RemoteMonsterManager` / `RemoteChestManager`가 각자 렌더링합니다.

## 4. 핵심 기능

### 4-1. 전투 (서버 판정)
```
클라이언트 공격 → Game_AttackRequest / Game_MonsterAttackRequest
  → 서버: 사거리·쿨다운·요청 빈도 검증 → 피해 = max(1, 공격력 − 방어력)
  → Game_MonsterDamageBroadcast / Game_MonsterDieBroadcast
  → 사망 시 경험치·골드·드랍 → 본인에게 Game_LootBroadcast
```
- 공격 **모션**(`Game_AttackAnimationRequest/Broadcast`)은 판정과 분리해 그대로 중계만 합니다.
- 몬스터는 반격하며, 플레이어가 죽으면 5초 뒤 서버가 자동 부활시킵니다(클라이언트 팝업 카운트다운과 같은 값).
- 몬스터는 5종(RedMushroom, Spider, Orc, Werewolf, Golem)이며 플레이어 능력치·장비 기준으로 밸런싱했습니다(`Server/몬스터_밸런싱_공식.txt`).

### 4-2. 킬 보상 저장
`KillRewardSaver`는 처치 보상을 세션과 분리된 백그라운드 작업으로 MainServer에 저장합니다.
- 실패하면 재시도합니다.
- 캐릭터별 순서를 보장해, 늦게 도착한 이전 보상이 새 레벨/경험치를 덮어쓰지 못합니다.
- 보상마다 ID(`KillRewardReceipt`)를 붙여 재시도가 중복 반영되지 않게 합니다.
- 처치 직후 접속이 끊겨도 저장이 취소되지 않습니다.

### 4-3. 아이템·장비
```
ItemDatabaseSO (Unity에서 설계)
   └ ItemDefinitionValidator.Generate() ──► Server ItemDefinitions.json (자동 생성)
```
- 원본은 클라이언트 `ItemDatabaseSO` 한 곳이며, 서버 JSON은 생성물이라 두 데이터가 어긋나지 않습니다.
- 장비 슬롯은 **Weapon / Armor / Helmet / Accessory**입니다. 제거된 슬롯(방패, 신발)의 enum 번호는 직렬화 값 보호를 위해 재사용하지 않습니다.
- **장착 흐름**: 인벤토리 UI → `PUT /equipment` → MainServer가 슬롯 유효성과 장착 가능 여부를 검증하고 같은 슬롯의 기존 장착을 자동 해제 → GameServer에 `Game_StatUpdateRequest`로 갱신된 능력치를 알림.
- **외형 동기화**: `Game_EquipmentChangedBroadcast`로 다른 플레이어에게 무기·갑옷·투구 변경을 전달합니다. 투구를 쓰면 헤어를 숨기고, 벗으면 기억해 둔 헤어를 복원합니다.
- **로비 프리뷰**: 캐릭터 선택 화면에서도 마지막으로 장착한 장비가 그대로 보입니다.

### 4-4. 물약과 재사용 대기시간
```
Game_UseItemRequest → 서버: 쿨다운 예약 → MainServer에서 1개 소비
   ├ 성공: 체력 회복 + Game_UseItemResult(남은 대기시간 ms)
   └ 실패: 예약 취소 + 실패 사유(UseItemFailReason)
```
- 모든 물약이 **하나의 쿨다운을 공유**하며, 회복 속도 상한(약 5%/초)을 두어 물약 연타로 체력을 무한 회복하는 것을 막습니다.
- 클라이언트는 남은 시간을 받아 사용 버튼에 카운트다운을 표시합니다.

### 4-5. 보물상자 (수동 후보 + 서버 무작위 선택)
```
[맵 프리팹]  TreasureChestSpawnPointMarker (후보 지점 + 등급 키)
     └ MapDataExporter ──► Server SpawnPoints / MapData JSON
[GameServer] 방 생성 시 ChestSpawnSelector가 등급별 개수만큼 후보에서 무작위 선택 (RoomChestState)
     └ Game_ActiveChestsNotify(입장 시) / Game_ChestSpawn·Despawn·OpenBroadcast
[Client]     RemoteChestManager는 서버가 알린 것만 렌더링
```
- **선착순 개봉**: 상자 하나는 한 번만 성공하며, 보상은 연 사람에게만 가고 열림 사실은 방 전체에 알립니다.
- **리스폰(이동형)**: 열린 상자는 일정 시간 뒤 사라지고, 같은 등급의 다른 후보 지점에 새로 생깁니다.
- 등급(`ChestLootTableKey`: Basic / Hidden / Rare)마다 드랍 테이블이 다르며, 아이템 등급을 섞는 로직이 있습니다.

### 4-6. 접속 안정성과 보안
- **중복 접속**: 같은 캐릭터가 다시 접속하면 `System_Kicked`로 기존 연결을 끊습니다(`SessionRegistry`).
- **요청 제한**: `RequestRateLimiter`가 과도한 요청을 차단합니다.
- **끊김 복구**: `DisconnectedPlayerStateStore`가 끊긴 플레이어의 상태를 보관합니다.
- **위치 보정**: `Game_PositionCorrection`으로 서버가 위치를 바로잡습니다.

## 5. 클라이언트 구조

| 계층 | 위치 | 역할 |
|---|---|---|
| Model | `Model/Character`, `Model/Combat` | `PlayerCharacterModel`, `EquipmentController`, `HealthComponent`, `CombatStatComponent` |
| View | `View/UI` | `UI_InventoryView`, `UI_LobbySceneView`, `CharacterPreviewStage` 등 표시 전용 |
| Presenter | `Presenter/Scene` | `GameSceneManager`, `LobbySceneManager` 등 씬 흐름과 서버 결과 연결 |
| Modules | `Modules` | `SaveDataManager`(REST), `GameServerConnectManager`(TCP), `ServerConnectManager`(HTTP 토큰), `ItemDatabaseManager`, `Remote*Manager` |
| Controller | `Controller` | 입력, 이동, 공격, 상호작용, 카메라 |

공통 기반은 `SingletonObject<T>`, Addressables(프리팹/DB 로딩), 오브젝트 풀입니다.

## 6. 데이터 원본과 흐름

| 데이터 | 원본(권위) | 경로 |
|---|---|---|
| 계정·캐릭터·인벤토리·장착 | MainServer DB | Client ⇄ REST, GameServer는 내부 API로 읽기/보상 쓰기 |
| 아이템 정의 | Client `ItemDatabaseSO` | Generate → 서버 JSON (부팅 시 1회 로드) |
| 몬스터 스폰·드랍 | 서버 JSON (`SpawnPoints`, `DropTables`) | 프리팹 마커 → Exporter → JSON |
| 위치·체력·전투 결과 | GameServer 메모리 | TCP 방송 → 클라이언트 표시 |

### 프로토콜
- 패킷은 `Shared/Networking/Packets`에 정의하고 `BinaryPacket.Write/Read`로 직렬화합니다.
- OpCode는 **상위 1바이트 = 도메인, 하위 1바이트 = 액션**(`0x{Domain}{Action}`)입니다. 0x00 시스템, 0x01 Auth, 0x02 Game, 0x03 Dungeon.
- 폐기된 OpCode 번호는 재사용하지 않습니다.
- 클라이언트 쪽 대응은 `GameOpCode.cs`, `GameBinaryPacket`입니다.

## 7. 실행 방법

사전 준비: .NET 8 SDK, MySQL, Unity 6000.4.8f1.
DB 연결 문자열, JWT 키, 내부 API 키는 저장소의 `appsettings.json`에 채워 두지 않았으므로 로컬 환경에 맞게 설정해야 합니다.

```bash
# 1) MainServer (HTTPS 58208/58209)
cd Server/MainServer/MainServer
dotnet run

# 2) GameServer (TCP 9000)
cd Server/MainServer/GameServer
dotnet run

# 테스트
cd Server/MainServer/GameServer.Tests
dotnet test
```

- 서버 실행 중에는 빌드 산출물이 잠겨 테스트가 실패할 수 있으므로, 테스트 전에 서버를 종료하세요.
- `ItemDefinitions.json`, `DropTables.json`은 부팅 시 1회만 로드되므로 수정 후 GameServer를 재시작해야 합니다.
- 클라이언트는 `Client/`를 Unity로 열어 Bootstrap 씬부터 실행합니다.

## 8. 관련 문서
- `Server/몬스터_밸런싱_공식.txt` — 플레이어·몬스터 밸런싱 공식과 물약 표
- `Server/몬스터_드랍_설정_가이드.txt` — 몬스터 드랍 아이템/재화 설정 절차
- `Server/클라우드_배포_가이드.txt` — 배포 가이드
