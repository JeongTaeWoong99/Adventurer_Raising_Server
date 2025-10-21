# 3D RPG Server

## 📋 목차
- [개요](#-개요)
- [관련 링크](#-관련-링크)
- [주요 기능](#-주요-기능)
- [프로젝트 구조](#-프로젝트-구조)
- [기술 스택](#-기술-스택)
- [아키텍처](#-아키텍처)
- [성능 특성](#-성능-특성)

## 📖 개요

| 항목 | 내용 |
|---|---|
| **기간** | 2025.05 ~ 2025.08 |
| **인원** | 1인 개발 |
| **역할** | 클라이언트, 서버, DB |
| **도구** | UNITY, C#, TCP SOCKET, FIREBASE |
| **타겟 기기** | PC |

Unity 클라이언트와 통신하는 **C# 데디케이트 서버**입니다.

비동기 TCP 소켓 통신, XML 기반 패킷 자동 생성, Firebase 연동을 통해 실시간 멀티플레이어 3D RPG 게임을 지원합니다.

## 🔗 관련 링크

| 항목            | 링크 |
|---------------|---|
| **클라 GitHub** | [바로가기](https://github.com/JeongTaeWoong99/Adventurer_Raising/tree/main) |

## ✨ 주요 기능

### 🔄 실시간 멀티플레이어
- **비동기 TCP 소켓 통신** : `SocketAsyncEventArgs` 기반 고성능 I/O
- **40FPS 서버 틱** : 25ms 간격으로 게임 상태 동기화
- **GameRoom 기반 씬 관리** : 씬별 독립적인 게임 세션 운영
- **플레이어/몬스터/오브젝트 동기화** : 실시간 위치, 애니메이션, 상태 브로드캐스트

### 📦 자동 패킷 생성 시스템
- **XML 기반 패킷 정의** : PDL.xml에서 선언적 패킷 정의
- **코드 자동 생성** : PacketGenerator가 C# 클래스 자동 생성
  - 패킷 클래스 (직렬화/역직렬화 포함)
  - ClientPacketManager / ServerPacketManager
  - 패킷 ID 열거형 및 핸들러 등록
- **휴먼 에러 제거** : 수작업 패킷 코딩 대비 실수 방지

### 🎯 게임 시스템

#### 전투 시스템
- **다양한 공격 타입** :
  - `immediate` : 즉시 데미지 적용
  - `move` : 투사체 이동 후 충돌
  - `continue` : 지속 범위 데미지
  - `buff` : 무적, 데미지 증가, 이동속도 버프
- **충돌 감지** : Circle vs Circle, OBB(Oriented Bounding Box) vs Circle
- **히트 판정** : 넉백, 피격 애니메이션, 데미지 계산
- **경험치 획득** : 몬스터 처치 시 경험치 드랍 및 레벨업

#### 몬스터 AI
- **상태 기반 행동** : Idle → 플레이어 감지 → Run(추적) → Attack
- **자동 전투** : 공격 범위 내 진입 시 자동 공격 (쿨다운 적용)
- **자연스러운 이동** : 랜덤 오프셋 적용으로 직선 이동 방지
- **서버 권위** : 모든 AI 로직 서버에서 처리

#### 스폰 관리
- **자동 스폰** : 씬별 몬스터/오브젝트 초기 배치
- **리스폰 시스템** :
  - 2단계 리스폰 (5초 Leave 애니메이션 + 5초 대기 = 10초)
  - mmNumber 기반 O(1) 위치 조회
- **씬 데이터 기반** : Firebase에서 스폰 위치, 개수, 반경 로드

#### 스케줄링 시스템
- **애니메이션 타이밍** : 공격, 피격, 이동 애니메이션 자동 전환
- **자동 공격** : 오브젝트(트랩) 반복 공격 스케줄링
- **리스폰 예약** : 사망 후 정확한 시간에 재생성

#### 실시간 채팅
- 게임 내 텍스트 채팅 시스템

### 🔐 데이터베이스 및 인증
- **Firebase Authentication** : 이메일 기반 로그인/회원가입
- **Firebase Realtime Database** : 플레이어 세션 데이터 (위치, HP, 레벨, 경험치)
- **Cloud Firestore** : 게임 설정 데이터 (캐릭터 스탯, 공격 정보, 스폰 설정)
- **JSON 로컬 캐싱** : 게임 데이터 딕셔너리 기반 빠른 조회

### 🎮 플레이어 시스템
- **레벨업 시스템** : 경험치 누적 → 레벨업 → 스탯 재계산 (HP, 데미지, 이동속도)
- **씬 전환** : 포털/스폰 포인트 기반 씬 이동
- **사망 처리** : Village 씬으로 리스폰, HP 전체 회복
- **데이터 영속성** : 접속 종료 시 Realtime DB에 자동 저장

## 📂 프로젝트 구조

```
3D_RPG_Server/
├── PacketGenerator/          # 패킷 자동 생성 도구
│   ├── Program.cs            # XML 파서 및 코드 생성 엔진
│   ├── PacketFormat.cs       # C# 코드 템플릿
│   ├── PDL.xml               # 패킷 정의 언어 (Packet Definition Language)
│   └── bin/
│       ├── GenPackets.cs     # 생성된 패킷 클래스
│       ├── ClientPacketManager.cs
│       └── ServerPacketManager.cs
│
├── Server/                   # 게임 서버 메인 로직
│   ├── Program.cs            # 서버 진입점
│   ├── GameRoom.cs           # 게임 룸 관리 (JobQueue 기반)
│   ├── Session/
│   │   ├── ClientSession.cs      # 플레이어 세션
│   │   ├── MonsterSession.cs     # 몬스터 세션
│   │   ├── ObjectSession.cs      # 오브젝트 세션
│   │   ├── CommonSession.cs      # 공통 엔티티 베이스
│   │   └── SessionManager.cs     # 세션 관리자
│   ├── DB/
│   │   ├── DBManager.cs          # 데이터 통합 관리
│   │   ├── AuthManager.cs        # Firebase Authentication
│   │   ├── RealTimeManager.cs    # Firebase Realtime DB
│   │   └── FirestoreManager.cs   # Cloud Firestore
│   ├── Packet/
│   │   ├── PacketHandler.cs      # 패킷 비즈니스 로직 처리
│   │   └── ServerPacketManager.cs
│   ├── AttackManager.cs      # 전투 시스템 (충돌, 데미지, 히트)
│   ├── SpawnManager.cs       # 스폰 관리 (초기 스폰, 리스폰)
│   ├── ScheduleManager.cs    # 시간 기반 작업 스케줄링
│   └── Utils/
│       ├── Define.cs         # 상수 및 열거형
│       └── Extension.cs      # 확장 메서드
│
├── ServerCore/              # 네트워크 코어 라이브러리
│   ├── Listener.cs          # TCP 리스너
│   ├── Session.cs           # 세션 베이스 클래스 (비동기 소켓)
│   ├── RecvBuffer.cs        # 수신 링 버퍼 (64KB)
│   ├── SendBuffer.cs        # 송신 버퍼 (동적 할당)
│   ├── JobQueue.cs          # 단일 스레드 작업 큐
│   ├── PriorityQueue.cs     # 우선순위 큐
│   └── Connector.cs         # 클라이언트 연결 헬퍼
│
├── Data/                     # 게임 데이터 (JSON)
│   ├── CharacterInfoData.json
│   ├── AttackInfoData.json
│   ├── MonsterSceneSettingData.json
│   ├── ObjectSceneSettingData.json
│   └── NetworkRoomSceneData.json
│
└── DummyClient/             # 서버 테스트 클라이언트
```

## 🛠 기술 스택

### 언어 및 프레임워크
- **C#** (.NET 9.0)
- **.NET SDK**

### 네트워킹
- **비동기 TCP 소켓** : `SocketAsyncEventArgs` 기반 고성능 I/O
- **커스텀 패킷 프로토콜** : 헤더(Size + PacketId) + Payload

### 데이터베이스
- **Firebase Realtime Database** : 플레이어 세션 데이터
- **Cloud Firestore** : 게임 설정 데이터
- **Firebase Authentication** : 사용자 인증

### 데이터 포맷
- **JSON** : Newtonsoft.Json (게임 데이터 직렬화)
- **XML** : 패킷 정의 언어

### 디자인 패턴
- **Actor Model** : GameRoom별 단일 스레드 메시지 큐 (JobQueue)
- **Command Pattern** : Action 객체로 작업 캡슐화 및 지연 실행
- **Facade Pattern** : ScheduleManager, AttackManager, SpawnManager로 복잡한 로직 단순화

## 🏗 아키텍처

### 패킷 처리 플로우

```
[클라이언트]
    ↓ TCP 패킷 전송
[Listener] → [ClientSession]
    ↓ OnRecvPacket
[ServerPacketManager] → 패킷 역직렬화
    ↓
[PacketHandler] → 비즈니스 로직 처리
    ↓
[GameRoom] → JobQueue.Push (단일 스레드 큐에 추가)
    ↓
[JobQueue.Flush] → 25ms마다 순차 실행
    ↓
[Broadcast] → 모든 클라이언트에게 동기화
```

### 핵심 설계 원리

#### 1️⃣ **ScheduleManager - 고효율 시간 관리**

**기존 문제점** : 각 엔티티별 개별 타이머 생성 시 스레드 분산 및 리소스 낭비

**해결 방안** :
- **단일 타이머** : 100ms 주기 하나의 타이머로 모든 작업 관리
- **이벤트 기반** : OS가 타이머 이벤트를 알려줄 때만 동작 (폴링 없음)
- **직접 실행** : 큐 처리 없이 콜백에서 즉시 실행 (지연 최소화)
- **리스트 기반** : 빠른 순회와 완료된 작업 정리

**활용 사례** :
- **애니메이션 전환** : Hit → Idle, Attack → Idle 자동 전환
- **몬스터 AI** : 100ms마다 플레이어 탐지 및 추적 로직
- **리스폰 스케줄링** : 사망 후 정확한 시간에 재생성
- **트랩 자동 공격** : 쿨다운 기반 반복 공격

**Server/ScheduleManager.cs:73-96** - 타이머 초기화 및 이벤트 핸들러

```csharp
private void InitializeTimer()
{
    _lifecycleTimer = new Timer(100);              // 100ms 단일 타이머
    _lifecycleTimer.Elapsed += OnTimerElapsed;     // 이벤트 기반 실행
    _lifecycleTimer.Start();
}

private void OnTimerElapsed(object sender, ElapsedEventArgs e)
{
    DateTime now = DateTime.UtcNow;
    lock (_lock)
    {
        ExecuteScheduledTasks(now);      // 스케줄 작업 직접 실행
        UpdateAnimationStates(now);      // 애니메이션 상태 업데이트
    }
}
```

#### 2️⃣ **PacketGenerator - 자동화된 네트워크 프로토콜**

**기존 문제점** : 수작업 패킷 코딩 시 클라이언트-서버 불일치, 반복 작업, 휴먼 에러

**해결 방안** :
- **XML 정의** : PDL.xml에서 선언적으로 패킷 구조 정의
- **배치 파일 실행** : 빌드 전 자동으로 패킷 코드 생성
- **클라이언트/서버 동기화** : 동일한 XML에서 양쪽 코드 생성
- **템플릿 기반** : PacketFormat.cs의 템플릿으로 일관된 코드 생성

**생성되는 파일** :
1. `GenPackets.cs` : 모든 패킷 클래스 (직렬화/역직렬화 포함)
2. `ClientPacketManager.cs` : 클라이언트용 패킷 매니저
3. `ServerPacketManager.cs` : 서버용 패킷 매니저

**PacketGenerator/Program.cs:17-64** - XML 파싱 및 코드 생성

```csharp
// PDL.xml 읽기 → 패킷 클래스 생성 → Manager 등록 자동화
using (XmlReader r = XmlReader.Create(pdlPath, settings))
{
    while (r.Read())
    {
        if (r.Depth == 1 && r.NodeType == XmlNodeType.Element)
            ParsePacket(r);  // 각 패킷 파싱 및 코드 생성
    }

    // 생성된 코드를 파일에 저장
    File.WriteAllText("GenPackets.cs", fileText);
    File.WriteAllText("ClientPacketManager.cs", clientManagerText);
    File.WriteAllText("ServerPacketManager.cs", serverManagerText);
}
```

**효과** :
- 패킷 정의 변경 시 단일 XML 수정으로 클라이언트/서버 동시 업데이트
- 직렬화/역직렬화 코드 자동 생성으로 실수 제거
- 패킷 ID 충돌 방지 (자동 증가)
- 개발 시간 대폭 단축

#### 3️⃣ **JobQueue - 단일 스레드 동기화**

**설계 목적** : 멀티스레드 환경에서 데이터 레이스 없이 안전한 게임 로직 처리

**동작 원리** :
- GameRoom별 독립적인 작업 큐
- 모든 게임 로직을 `Action`으로 큐에 Push
- 25ms마다 `Flush()`로 단일 스레드에서 순차 실행
- 락 없는 게임 로직 (큐 Push/Pop에만 락 사용)

**ServerCore/JobQueue.cs** - 작업 큐 구현
**Server/GameRoom.cs:40-65** - 40FPS Flush를 통한 동기화

```csharp
// 플레이어 이동 예시
public void Move(ClientSession session, C_EntityMove movePacket)
{
    // JobQueue에 작업 추가 (멀티스레드 안전)
    Push(() =>
    {
        // 단일 스레드에서 실행되므로 락 불필요
        session.PosX = movePacket.posX;
        session.PosY = movePacket.posY;
        session.PosZ = movePacket.posZ;

        // 모든 클라이언트에 브로드캐스트
        S_BroadcastEntityMove broadcast = new S_BroadcastEntityMove();
        // ... 패킷 구성
        Broadcast(broadcast);
    });
}
```

#### 4️⃣ **Session 계층 구조**

엔티티 타입별 공통 기능과 특화 기능 분리 :

```
Session (ServerCore)
  ↓ [비동기 소켓 I/O]
PacketSession
  ↓ [패킷 파싱 및 핸들러 라우팅]
CommonSession
  ↓ [공통 엔티티 속성: PosX/Y/Z, HP, AnimationId 등]
├── ClientSession  (EntityType = Player)
├── MonsterSession (EntityType = Monster)
└── ObjectSession  (EntityType = Object/Trap)
```

**ServerCore/Session.cs:69-297** - 비동기 소켓 통신 베이스
**Server/Session/ClientSession.cs:12-76** - 플레이어 세션 구현

#### 5️⃣ **비동기 소켓 통신**

고성능 네트워크 I/O를 위한 최적화 :

- **SocketAsyncEventArgs** : Zero-allocation 비동기 패턴
- **RecvBuffer (64KB)** : 링 버퍼로 패킷 단편화 처리
- **SendBuffer** : 동적 할당 및 멀티 세그먼트 배치 전송
- **모아보내기** : 25ms 윈도우에서 여러 패킷 배치 전송 (syscall 감소)

**ServerCore/Listener.cs:8-97** - 클라이언트 Accept 처리

### 보조 시스템

#### mmNumber 기반 고속 조회

**문제** : 리스폰 시 O(n) 스폰 위치 탐색

**해결** : `Dictionary<int, SpawnData> _monsterMmNumberDict`로 O(1) 조회

**Server/SpawnManager.cs:19-20** - mmNumber 딕셔너리

#### 충돌 감지 시스템

**AttackManager**가 다양한 충돌 타입 지원 :
- **Circle vs Circle** : 반경 기반 충돌
- **OBB vs Circle** : 회전 적용 박스 vs 원 충돌

**Server/AttackManager.cs** - 충돌 감지 및 데미지 처리

## 📊 성능 특성

| 항목 | 수치 | 설명 |
|------|------|------|
| **서버 틱레이트** | 40 FPS (25ms) | GameRoom.Flush() 실행 주기 |
| **스케줄 체크** | 100ms | ScheduleManager 타이머 주기 |
| **동시 접속** | 룸별 독립 | GameRoom별 독립적인 JobQueue로 확장 가능 |
| **패킷 처리** | 비동기 + 단일스레드 | 높은 처리량 + 데이터 안정성 확보 |
| **RecvBuffer** | 64KB | 링 버퍼로 패킷 단편화 처리 |
| **SendBuffer** | 동적 할당 | 필요시 자동 확장 |
| **DB 조회** | 딕셔너리 캐싱 | O(1) 캐릭터/공격 정보 조회 |
| **리스폰 조회** | mmNumber 딕셔너리 | O(1) 스폰 위치 조회 |

### 최적화 기법

- **단일 스레드 게임 로직** : 락 경합 제거
- **배치 브로드캐스트** : 25ms 윈도우에서 패킷 모아보내기
- **링 버퍼** : 수신 패킷 allocation 최소화
- **이벤트 기반 스케줄링** : 폴링 없는 시간 관리
- **mmNumber 인덱싱** : 리스폰 위치 즉시 조회
- **JSON 딕셔너리 캐싱** : 게임 데이터 메모리 상주