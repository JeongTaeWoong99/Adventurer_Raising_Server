using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Net;

namespace Server
{
    public class SpawnManager
    {
        static SpawnManager _instance = new SpawnManager();
        public static SpawnManager Instance { get { return _instance; } }

        private Dictionary<string, CharacterInfoData>             _characterInfos       = new Dictionary<string, CharacterInfoData>();              // 플레이어/몬스터/오브젝트 정보
        private Dictionary<string, List<ObjectSceneSettingData>>  _objectSceneSettings  = new Dictionary<string, List<ObjectSceneSettingData>>();   // 오브젝트 씬 세팅 정보 
        private Dictionary<string, List<MonsterSceneSettingData>> _monsterSceneSettings = new Dictionary<string, List<MonsterSceneSettingData>>();  // 몬스터 씬 세팅 정보
        
        // NEW: mmNumber 기반 빠른 검색용 딕셔너리
        private Dictionary<string, ObjectSceneSettingData>  _objectMmNumberDict  = new Dictionary<string, ObjectSceneSettingData>();   // mmNumber → ObjectSceneSetting
        private Dictionary<string, MonsterSceneSettingData> _monsterMmNumberDict = new Dictionary<string, MonsterSceneSettingData>(); // mmNumber → MonsterSceneSetting

        // 파이어 베이스에서 받아온 정보로 정보 세팅
        public void Init(List<CharacterInfoData> characterInfos, List<ObjectSceneSettingData> objectSettings, List<MonsterSceneSettingData> monsterSettings)
        {
            // serialNumber + level 조합을 key로 사용
            _characterInfos = characterInfos.ToDictionary(info => $"{info.serialNumber}_{info.level}");

            _objectSceneSettings = objectSettings
                                  .GroupBy(setting => setting.sceneName)
                                  .ToDictionary(group => group.Key, group => group.ToList());

            _monsterSceneSettings = monsterSettings
                                   .GroupBy(setting => setting.sceneName)
                                   .ToDictionary(group => group.Key, group => group.ToList());
            
            // mmNumber 기반 빠른 검색용 딕셔너리 초기화
            _objectMmNumberDict  = objectSettings.ToDictionary(setting => setting.mmNumber);
            _monsterMmNumberDict = monsterSettings.ToDictionary(setting => setting.mmNumber);
        }

        #region 서버 런 후, 초기 세팅

        // 서버가 켜지고, 1회 세팅
        public void DefaultSceneEntitySetting()
        {
            // 만들어진 방과 데이터의 sceneName를 비교하여, 동일하면, 해당 방에 생성한다...
            foreach (var room in Program.GameRooms.Values)
            {
                // 해당 룸(씬)에 맞는 오브젝트들을 스폰합니다.
                if (_objectSceneSettings.TryGetValue(room.SceneName, out var objectSettings))
                {
                    foreach (var setting in objectSettings)
                    {
                        SpawnObject(setting, room);
                    }
                }

                // 해당 룸(씬)에 맞는 몬스터들을 스폰합니다.
                if (_monsterSceneSettings.TryGetValue(room.SceneName, out var monsterSettings))
                {
                    foreach (var setting in monsterSettings)
                    {
                        SpawnMonster(setting, room);
                    }
                }
            }
            // Console.WriteLine("모든 씬의 몬스터 및 오브젝트 스폰 완료!");
        }

        // 오브젝트 생성
        private void SpawnObject(ObjectSceneSettingData setting, GameRoom room)
        {
            // 오브젝트/몬스터는 기본적으로 level 1로 처리
            string key = $"{setting.serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 ObjectInfo를 찾을 수 없습니다.");
                return;
            }

            var newSession = SessionManager.Instance.ObjectSessionGenerate();

            // 공통 정보 세팅
            newSession.SerialNumber = setting.serialNumber;
            newSession.MmNumber = setting.mmNumber; // NEW: 관리번호 설정
            newSession.NickName     = info.nickName;
            if (int.TryParse(info.maxHp, out int maxHp))
                newSession.CurrentHP = maxHp;
            if (bool.TryParse(info.invincibility?.Trim().ToLower(), out bool invincibility))
                newSession.Invincibility = invincibility;
            if (float.TryParse(info.body_Size, out float bSize))
                newSession.Body_Size = bSize;
            if (float.TryParse(info.moveSpeed, out float mv))
                newSession.moveSpeed = mv;
            if (int.TryParse(info.normalAttackDamage, out int damage))
                newSession.Damage = damage;
            if (int.TryParse(info.level, out int level))
                newSession.CurrentLevel = level;
            if (float.TryParse(info.hitLength, out float hitLen))
                newSession.hitLength = hitLen;
            if (float.TryParse(info.findRadius, out float findRad))
                newSession.findRadius = findRad;
            if (int.TryParse(info.dropExp, out int dExp))
                newSession.dropExp = dExp;
                
            // 애니메이션 정보는 AttackInfoData에서 실시간으로 참조하므로 별도 설정 불필요

            // 위치 정보 세팅(고정)
            Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
            newSession.PosX = spawnPos.X;
            newSession.PosY = spawnPos.Y;
            newSession.PosZ = spawnPos.Z;

            // 룸에 넣어주기
            newSession.Room = room;
            room._commonSessions.Add(newSession);

            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));
            
            // NEW: ScheduleManager에 자동 공격 엔티티 애니메이션 초기화 (트랩 전용)
            if (setting.serialNumber.StartsWith("O")) // 모든 오브젝트 (트랩)
            {
                ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
            }
            
            // ☆ 먼저 들어와 있던 클라이언트들 모두에게, 새로운 엔티티의 입장을 알린다.
            // ☆ 나중에 해당 부분에 바리에이션을 넣어서, 랜덤 위치 생성....
            S_BroadcastEntityEnter enter = new S_BroadcastEntityEnter
            {
                entityType    = newSession.EntityType,
                ID			  = newSession.SessionId,
                serialNumber  = newSession.SerialNumber,
                nickname      = newSession.NickName,
                currentLevel  = newSession.CurrentLevel,
                currentHp     = newSession.CurrentHP,
                live          = newSession.Live,
                invincibility = newSession.Invincibility,
                posX          = newSession.PosX,
                posY          = newSession.PosY,
                posZ          = newSession.PosZ,
                rotationY     = newSession.RotationY,
                animationID   = newSession.AnimationId
            };
            room.Broadcast(enter.Write()); // 모든 클라에게 보내주기 위해서,
                                                  // Broadcast를 통해, _pendingList에 등록하고,
                                                  // 순차적으로 보내줌.(다중 세그먼트 Send 사용)
        }
        
        // 몬스터 생성
        private void SpawnMonster(MonsterSceneSettingData setting, GameRoom room)
        {
            // 오브젝트/몬스터는 기본적으로 level 1로 처리
            string key = $"{setting.serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }

            if (!int.TryParse(setting.spawnNumber, out int spawnCount))
                return;

            for (int i = 0; i < spawnCount; i++)
            {
                var newSession = SessionManager.Instance.MonsterSessionGenerate();

                // 공통 정보 세팅
                newSession.SerialNumber = setting.serialNumber;
                newSession.MmNumber = setting.mmNumber; // NEW: 관리번호 설정
                newSession.NickName = info.nickName;
                if (int.TryParse(info.maxHp, out int maxHp))
                    newSession.CurrentHP = maxHp;
                if (bool.TryParse(info.invincibility?.Trim().ToLower(), out bool invincibility))
                    newSession.Invincibility = invincibility;
                if (float.TryParse(info.body_Size, out float bSize))
                    newSession.Body_Size = bSize;
                if (float.TryParse(info.moveSpeed, out float mv))
                    newSession.moveSpeed = mv;
                if (int.TryParse(info.normalAttackDamage, out int damage))
                    newSession.Damage = damage;
                if (int.TryParse(info.level, out int level))
                    newSession.CurrentLevel = level;
                if (float.TryParse(info.hitLength, out float hitLen))
                    newSession.hitLength = hitLen;
                if (float.TryParse(info.findRadius, out float findRad))
                    newSession.findRadius = findRad;
                if (int.TryParse(info.dropExp, out int dExp))
                    newSession.dropExp = dExp;
                    
                // 애니메이션 정보는 AttackInfoData에서 실시간으로 참조하므로 별도 설정 불필요

                // 위치 정보 세팅(중심값 + 범위 안에서 랜덤값을 더해줌)
                Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
                if (float.TryParse(setting.makeRadius, out float radius))
                {
                    // 각 몬스터마다 고유한 시드 사용
                    Random rand = new Random((int)(DateTime.UtcNow.Ticks + newSession.SessionId + i));
                    double angle = rand.NextDouble() * 2 * Math.PI;
                    double r = radius * Math.Sqrt(rand.NextDouble());

                    newSession.PosX = spawnPos.X + (float)(r * Math.Cos(angle));
                    newSession.PosY = spawnPos.Y;
                    newSession.PosZ = spawnPos.Z + (float)(r * Math.Sin(angle));
                }

                // 룸에 넣어주기
                newSession.Room = room;
                room._commonSessions.Add(newSession);

                newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));
                
                // NEW: ScheduleManager에 자동 공격 엔티티 애니메이션 초기화 (몬스터)
                if (setting.serialNumber.StartsWith("M")) // 모든 몬스터
                {
                    ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
                }
                
                // ☆ 먼저 들어와 있던 클라이언트들 모두에게, 새로운 엔티티의 입장을 알린다.
                // ☆ 나중에 해당 부분에 바리에이션을 넣어서, 랜덤 위치 생성....
                S_BroadcastEntityEnter enter = new S_BroadcastEntityEnter
                {
                    entityType    = newSession.EntityType,
                    ID			  = newSession.SessionId,
                    serialNumber  = newSession.SerialNumber,
                    nickname      = newSession.NickName,
                    currentLevel  = newSession.CurrentLevel,
                    currentHp     = newSession.CurrentHP,
                    live          = newSession.Live,
                    invincibility = newSession.Invincibility,
                    posX          = newSession.PosX,
                    posY          = newSession.PosY,
                    posZ          = newSession.PosZ,
                    rotationY     = newSession.RotationY,
                    animationID   = newSession.AnimationId
                };
                room.Broadcast(enter.Write()); // 모든 클라에게 보내주기 위해서,
                                                      // Broadcast를 통해, _pendingList에 등록하고,
                                                      // 순차적으로 보내줌.(다중 세그먼트 Send 사용)
            }
        }
        

        #endregion
        
        #region mmNumber 기반 고속 재생성 시스템
        
        /// <summary>
        /// mmNumber 기반 빠른 재생성 시스템
        /// - 복잡한 거리 계산 없이 mmNumber로 직접 원래 스폰 정보 참조
        /// - O(1) 시간복잡도로 빠른 검색 및 재생성
        /// - 오브젝트: 정확한 위치 재생성 / 몬스터: 스폰 영역 내 랜덤 재생성
        /// </summary>
        public void RespawnByMmNumber(string mmNumber, string sceneName)
        {
            if (string.IsNullOrEmpty(mmNumber))
            {
                Console.WriteLine("[Error] mmNumber가 비어있습니다.");
                return;
            }

            // 오브젝트 재생성
            if (mmNumber.StartsWith("O") && _objectMmNumberDict.TryGetValue(mmNumber, out var objectSetting))
            {
                //Console.WriteLine($"[SpawnManager] 오브젝트 {mmNumber} 빠른 재생성");
                RespawnObjectFromSetting(objectSetting, sceneName);
                return;
            }

            // 몬스터 재생성
            if (mmNumber.StartsWith("M") && _monsterMmNumberDict.TryGetValue(mmNumber, out var monsterSetting))
            {
                //Console.WriteLine($"[SpawnManager] 몬스터 {mmNumber} 빠른 재생성");
                RespawnMonsterFromSetting(monsterSetting, sceneName);
                return;
            }

            Console.WriteLine($"[Error] mmNumber '{mmNumber}'에 해당하는 스폰 설정을 찾을 수 없습니다.");
        }
        
        /// <summary>
        /// 엔티티 정보 설정 - 파이어베이스 데이터 기반 세션 초기화
        /// - 파이어베이스에서 변환된 애니메이션 데이터 직접 설정 (하드코딩 제거)
        /// - 재생성 시 기본 상태로 초기화
        /// - NOTE: MmNumber는 호출하는 쪽에서 별도로 설정해야 함
        /// </summary>
        private void SetupEntitySession(CommonSession session, CharacterInfoData info, string serialNumber)
        {
            session.SerialNumber = serialNumber;
            session.NickName     = info.nickName;
            session.Live         = true;
            session.AnimationId  = 0;
            
            if (int.TryParse(info.maxHp, out int maxHp))
                session.CurrentHP = maxHp;
            if (bool.TryParse(info.invincibility?.Trim().ToLower(), out bool invincibility))
                session.Invincibility = invincibility;
            if (float.TryParse(info.body_Size, out float bSize))
                session.Body_Size = bSize;
            if (float.TryParse(info.moveSpeed, out float mv))
                session.moveSpeed = mv;
            if (int.TryParse(info.normalAttackDamage, out int damage))
                session.Damage = damage;
            if (int.TryParse(info.level, out int level))
                session.CurrentLevel = level;
            if (float.TryParse(info.hitLength, out float hitLen))
                session.hitLength = hitLen;
            if (float.TryParse(info.findRadius, out float findRad))
                session.findRadius = findRad;
            if (int.TryParse(info.dropExp, out int dExp))
                session.dropExp = dExp;
        }

        /// <summary>
        /// ObjectSceneSetting 기반 오브젝트 재생성
        /// - mmNumber로 찾은 원래 스폰 설정으로 정확한 위치에 재생성
        /// - 기존 SpawnObject 로직 재사용으로 안정성 보장
        /// </summary>
        private void RespawnObjectFromSetting(ObjectSceneSettingData setting, string sceneName)
        {
            if (!Program.GameRooms.TryGetValue(sceneName, out var room))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'을 찾을 수 없습니다.");
                return;
            }

            // 오브젝트 정보 가져오기
            string key = $"{setting.serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 ObjectInfo를 찾을 수 없습니다.");
                return;
            }

            var newSession = SessionManager.Instance.ObjectSessionGenerate();

            // 공통 정보 세팅 (mmNumber 포함)
            SetupEntitySession(newSession, info, setting.serialNumber);
            newSession.MmNumber = setting.mmNumber; // 관리번호 설정

            // 위치 정보 세팅 (정확한 원래 위치)
            Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
            newSession.PosX = spawnPos.X;
            newSession.PosY = spawnPos.Y;
            newSession.PosZ = spawnPos.Z;

            // 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // 자동 공격 엔티티 애니메이션 초기화 (트랩 전용)
            if (setting.serialNumber.StartsWith("O"))
            {
                ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
            }

            // 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);

            Console.WriteLine($"[SpawnManager] 오브젝트 {setting.mmNumber} 빠른 재생성 완료");
        }

        /// <summary>
        /// MonsterSceneSetting 기반 몬스터 재생성
        /// - mmNumber로 찾은 원래 스폰 설정으로 랜덤 위치에 재생성
        /// - 기존 SpawnSingleMonsterFromSetting 로직 재사용
        /// </summary>
        private void RespawnMonsterFromSetting(MonsterSceneSettingData setting, string sceneName)
        {
            if (!Program.GameRooms.TryGetValue(sceneName, out var room))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'을 찾을 수 없습니다.");
                return;
            }

            // 몬스터 정보 가져오기
            string key = $"{setting.serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }

            var newSession = SessionManager.Instance.MonsterSessionGenerate();

            // 공통 정보 세팅 (mmNumber 포함)
            SetupEntitySession(newSession, info, setting.serialNumber);
            newSession.MmNumber = setting.mmNumber; // 관리번호 설정

            // 랜덤 위치 생성 (고유 시드 사용)
            Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
            if (float.TryParse(setting.makeRadius, out float radius))
            {
                Random rand = new Random((int)(DateTime.UtcNow.Ticks + newSession.SessionId));
                double angle = rand.NextDouble() * 2 * Math.PI;
                double r = radius * Math.Sqrt(rand.NextDouble());

                newSession.PosX = spawnPos.X + (float)(r * Math.Cos(angle));
                newSession.PosY = spawnPos.Y;
                newSession.PosZ = spawnPos.Z + (float)(r * Math.Sin(angle));
            }
            else
            {
                newSession.PosX = spawnPos.X;
                newSession.PosY = spawnPos.Y;
                newSession.PosZ = spawnPos.Z;
            }

            // 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // 자동 공격 엔티티 애니메이션 초기화
            ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);

            // 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);

            Console.WriteLine($"[SpawnManager] 몬스터 {setting.mmNumber} 빠른 재생성 완료: ({newSession.PosX:F2}, {newSession.PosZ:F2})");
        }

        // 엔티티 입장 브로드캐스트
        private void BroadcastEntityEnter(CommonSession session, GameRoom room)
        {
            S_BroadcastEntityEnter enter = new S_BroadcastEntityEnter {
                entityType    = session.EntityType,
                ID            = session.SessionId,
                serialNumber  = session.SerialNumber,
                nickname      = session.NickName,
                currentLevel  = session.CurrentLevel,
                currentHp     = session.CurrentHP,
                live          = session.Live,
                invincibility = session.Invincibility,
                posX          = session.PosX,
                posY          = session.PosY,
                posZ          = session.PosZ,
                rotationY     = session.RotationY,
                animationID   = session.AnimationId
            };
            room.Broadcast(enter.Write());
        }

        #endregion
    }
}