## 📋 목차
- [개요](#-개요)
- [주요 기능](#-주요-기능)
- [프로젝트 구조](#-프로젝트-구조)
- [기술 스택](#-기술-스택)
- [시작하기](#-시작하기)
- [아키텍처](#-아키텍처)

## 📖 개요

이 프로젝트는 Unity 클라이언트와 통신하는 고성능 게임 서버입니다. 비동기 소켓 통신, 패킷 자동 생성, Firebase 연동을 통해 실시간 멀티플레이어 RPG 게임을 지원합니다.

## ✨ 주요 기능

### 🔄 실시간 멀티플레이어
- 비동기 TCP 소켓 통신 (40FPS 서버 틱)
- GameRoom 기반 씬별 세션 관리
- 플레이어/몬스터/오브젝트 동기화

### 📦 자동 패킷 생성 시스템
- XML 기반 패킷 정의 (PDL.xml)
- C# 패킷 클래스 자동 생성
- 클라이언트/서버 패킷 매니저 자동 생성
- 직렬화/역직렬화 코드 자동화

### 🎯 게임 시스템
- **전투 시스템** : 공격 판정, 데미지 계산, 히트박스 충돌 처리
- **경험치/레벨업** : 몬스터 처치 시 경험치 획득 및 레벨업
- **스폰 관리** : 몬스터/오브젝트 자동 스폰 및 리스폰
- **스케줄링** : 애니메이션 타이밍, 자동 공격, 리스폰 스케줄링
- **실시간 채팅** : 게임 내 채팅 시스템

### 🔐 인증 및 데이터 관리
- Firebase Authentication 연동
- Firebase Realtime Database (유저 데이터)
- Cloud Firestore (게임 설정 데이터)
- JSON 기반 게임 데이터 로드

## 📂 프로젝트 구조

```
3D_RPG_Server/
├── PacketGenerator/          # 패킷 자동 생성 도구
│   ├── Program.cs            # XML 파서 및 코드 생성기
│   ├── PacketFormat.cs       # C# 패킷 템플릿
│   └── bin/
│       ├── GenPackets.cs     # 생성된 패킷 클래스
│       ├── ClientPacketManager.cs
│       └── ServerPacketManager.cs
│
├── Server/                   # 게임 서버 메인 로직
│   ├── Program.cs            # 서버 진입점
│   ├── GameRoom.cs           # 게임 룸 관리
│   ├── Session/
│   │   ├── ClientSession.cs      # 플레이어 세션
│   │   ├── MonsterSession.cs     # 몬스터 세션
│   │   ├── ObjectSession.cs      # 오브젝트 세션
│   │   ├── CommonSession.cs      # 공통 세션 베이스
│   │   └── SessionManager.cs     # 세션 관리자
│   ├── DB/
│   │   ├── DBManager.cs          # 데이터 통합 관리
│   │   ├── AuthManager.cs        # Firebase Auth
│   │   ├── RealTimeManager.cs    # Firebase Realtime DB
│   │   └── FirestoreManager.cs   # Cloud Firestore
│   ├── Packet/
│   │   ├── PacketHandler.cs      # 패킷 처리 핸들러
│   │   └── ServerPacketManager.cs
│   ├── AttackManager.cs      # 전투 시스템
│   ├── SpawnManager.cs       # 스폰 관리
│   ├── ScheduleManager.cs    # 작업 스케줄링
│   └── Utils/
│       ├── Define.cs         # 상수 및 열거형
│       └── Extension.cs      # 확장 메서드
│
├── ServerCore/              # 네트워크 코어 라이브러리
│   ├── Listener.cs          # TCP 리스너
│   ├── Session.cs           # 세션 베이스 클래스
│   ├── RecvBuffer.cs        # 수신 버퍼
│   ├── SendBuffer.cs        # 송신 버퍼
│   ├── JobQueue.cs          # 단일 스레드 작업 큐
│   ├── PriorityQueue.cs     # 우선순위 큐
│   └── Connector.cs         # 클라이언트 연결 헬퍼
│
└── Data/                     # 게임 데이터 (JSON)
    ├── CharacterInfoData.json
    ├── AttackInfoData.json
    ├── MonsterSceneSettingData.json
    ├── ObjectSceneSettingData.json
    └── NetworkRoomSceneData.json
```

## 🛠 기술 스택

- **언어** : C# (.NET 9.0)
- **네트워킹** : Async Socket (TCP)
- **데이터베이스**:
  - Firebase Realtime Database
  - Cloud Firestore
- **인증** : Firebase Authentication
- **데이터 포맷** : JSON (Newtonsoft.Json)
- **빌드** : .NET SDK

## 🏗 아키텍처

### 패킷 처리 플로우
```
[클라이언트]
    ↓ TCP 패킷 전송
[Listener] → [ClientSession]
    ↓ OnRecvPacket
[PacketManager] → 패킷 역직렬화
    ↓
[PacketHandler] → 비즈니스 로직 처리
    ↓
[GameRoom] → JobQueue를 통한 단일 스레드 처리
    ↓
[Broadcast] → 모든 클라이언트에게 동기화
```

### 핵심 설계 패턴

#### 1. **PacketGenerator - 코드 자동 생성**
- XML 기반 패킷 정의로 유지보수성 향상
- 직렬화/역직렬화 코드 자동 생성
- 클라이언트/서버 동시 지원

**PacketGenerator/Program.cs:18-65** - XML 파싱 및 코드 생성 메인 로직
```csharp
// PDL.xml 읽기 → 패킷 클래스 생성 → Manager 등록 자동화
```

#### 2. **JobQueue - 단일 스레드 동기화**
- GameRoom별 작업 큐를 통한 동시성 제어
- 락 프리 단일 스레드 처리로 데이터 레이스 방지

**ServerCore/JobQueue.cs:14-77** - 작업 큐 구현
**Server/GameRoom.cs:40-65** - Flush를 통한 40FPS 동기화

#### 3. **Session 계층 구조**
```
Session (ServerCore)
  ↓
PacketSession (패킷 파싱)
  ↓
CommonSession (공통 엔티티 속성)
  ↓
ClientSession / MonsterSession / ObjectSession
```

**ServerCore/Session.cs:69-297** - 비동기 소켓 통신 베이스
**Server/Session/ClientSession.cs:12-76** - 플레이어 세션 구현

#### 4. **비동기 소켓 통신**
- `SocketAsyncEventArgs`를 활용한 고성능 비동기 I/O
- RecvBuffer/SendBuffer를 통한 효율적인 버퍼 관리
- 모아보내기 최적화

**ServerCore/Listener.cs:8-97** - 클라이언트 Accept 처리

## 📊 성능 특징

- **서버 틱레이트** : 40FPS (25ms 간격)
- **동시 접속** : 룸별 독립적인 JobQueue로 확장 가능
- **패킷 처리** : 비동기 소켓 + 단일 스레드 큐로 안정성 확보
- **버퍼 관리** : RecvBuffer 64KB, SendBuffer 동적 할당

## 🔗 관련 프로젝트

- [Unity 클라이언트](https://github.com/JeongTaeWoong99/Adventurer_Raising_Client) - 3D RPG 게임 클라이언트

## 📝 라이선스

이 프로젝트는 개인 학습 목적으로 제작되었습니다.

---

**개발자**: [@JeongTaeWoong99](https://github.com/JeongTaeWoong99)
