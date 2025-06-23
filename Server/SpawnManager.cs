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
        }

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

        #region NEW: 재생성 기능 (ScheduleManager 연동) - 역할 분리된 스폰 시스템
        /// <summary>
        /// NEW: 타입별 재생성 - 오브젝트는 정확한 위치, 몬스터는 스폰 영역 내 랜덤
        /// - 오브젝트: 사망한 위치에서 정확히 재생성 (전략적 요소)
        /// - 몬스터: 원래 스폰 영역 내에서 랜덤 재생성 (자연스러운 느낌)
        /// - 다중 스폰 포인트 문제 해결
        /// </summary>
        public void RespawnAtOriginalPosition(string serialNumber, string sceneName, float originalX, float originalY, float originalZ)
        {
            if (serialNumber.StartsWith("O")) // 오브젝트는 정확한 위치
            {
                //Console.WriteLine($"[SpawnManager] 오브젝트 {serialNumber} 정확한 위치에서 재생성: ({originalX}, {originalY}, {originalZ})");
                RespawnEntityAtPosition(serialNumber, sceneName, originalX, originalY, originalZ);
            }
            else if (serialNumber.StartsWith("M")) // 몬스터는 원래 스폰 영역 내 랜덤
            {
                //Console.WriteLine($"[SpawnManager] 몬스터 {serialNumber} 원래 스폰 영역에서 랜덤 재생성 (기준점: {originalX}, {originalY}, {originalZ})");
                RespawnMonsterInOriginalSpawnArea(serialNumber, sceneName, originalX, originalY, originalZ);
            }
        }

        /// <summary>
        /// NEW: 엔티티 재생성 - 시리얼넘버 타입별 재생성 방식 결정
        /// - O로 시작   : 오브젝트 재생성
        /// - M으로 시작 : 몬스터 재생성
        /// </summary>
        private void RespawnEntity(string serialNumber, string sceneName)
        {
            // NEW: 해당 씬의 룸 찾기
            if (!Program.GameRooms.TryGetValue(sceneName, out var room))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'을 찾을 수 없습니다.");
                return;
            }

            Console.WriteLine($"[SpawnManager] {serialNumber} 재생성 시작");

            // NEW: 시리얼넘버 타입에 따라 재생성 방식 결정
            if (serialNumber.StartsWith("O")) // 오브젝트 (트랩 등)
            {
                RespawnObject(serialNumber, room);
            }
            else if (serialNumber.StartsWith("M")) // 몬스터
            {
                RespawnMonster(serialNumber, room);
            }
        }

        /// <summary>
        /// NEW: 특정 위치에서 엔티티 재생성 - 원래 위치 복원
        /// - 사망한 엔티티의 정확한 위치에서 재생성
        /// - 다중 스폰 포인트 문제 해결
        /// </summary>
        private void RespawnEntityAtPosition(string serialNumber, string sceneName, float originalX, float originalY, float originalZ)
        {
            // NEW: 해당 씬의 룸 찾기
            if (!Program.GameRooms.TryGetValue(sceneName, out var room))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'을 찾을 수 없습니다.");
                return;
            }

            Console.WriteLine($"[SpawnManager] {serialNumber} 원래 위치에서 재생성 시작: ({originalX}, {originalY}, {originalZ})");

            // NEW: 시리얼넘버 타입에 따라 재생성 방식 결정 (위치 정보 전달)
            if (serialNumber.StartsWith("O")) // 오브젝트 (트랩 등)
            {
                RespawnObjectAtPosition(serialNumber, room, originalX, originalY, originalZ);
            }
            else if (serialNumber.StartsWith("M")) // 몬스터
            {
                RespawnMonsterAtPosition(serialNumber, room, originalX, originalY, originalZ);
            }
        }

        /// <summary>
        /// NEW: 오브젝트 재생성 - 트랩 등의 오브젝트 재생성
        /// - 원본 설정을 찾아서 동일한 위치에 재생성
        /// - 트랩의 경우 반복 공격 시스템 자동 활성화
        /// </summary>
        private void RespawnObject(string serialNumber, GameRoom room)
        {
            // NEW: 원본 오브젝트 설정 찾기
            if (!_objectSceneSettings.TryGetValue(room.SceneName, out var objectSettings))
                return;

            var originalSetting = objectSettings.FirstOrDefault(s => s.serialNumber == serialNumber);
            if (originalSetting == null)
            {
                Console.WriteLine($"[Error] 오브젝트 설정을 찾을 수 없습니다: {serialNumber}");
                return;
            }

            // NEW: 새로운 오브젝트 스폰 (기존 SpawnObject 메서드 재사용)
            SpawnObject(originalSetting, room);
            Console.WriteLine($"[SpawnManager] 오브젝트 {serialNumber} 재생성 완료");

            // NEW: 재생성된 오브젝트의 자동 공격 초기화 (트랩 등)
            if (serialNumber.StartsWith("O"))
            {
                var respawnedSession = room._commonSessions.LastOrDefault(s => s.SerialNumber == serialNumber);
                if (respawnedSession != null)
                {
                    ScheduleManager.Instance.SetAnimationState(respawnedSession, Define.Anime.Idle);
                }
            }
        }

        /// <summary>
        /// NEW: 특정 위치에서 오브젝트 재생성 - 원래 위치 복원
        /// - 사망한 오브젝트의 정확한 위치에서 재생성
        /// - 다중 스폰 포인트 문제 해결 (O000, O001 등)
        /// </summary>
        private void RespawnObjectAtPosition(string serialNumber, GameRoom room, float originalX, float originalY, float originalZ)
        {
            Console.WriteLine($"[SpawnManager] 오브젝트 {serialNumber} 원래 위치에서 재생성: ({originalX}, {originalY}, {originalZ})");

            // NEW: 오브젝트 정보 가져오기 (기존 방식과 동일)
            string key = $"{serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 ObjectInfo를 찾을 수 없습니다.");
                return;
            }

            // NEW: 새로운 오브젝트 세션 생성
            var newSession = SessionManager.Instance.ObjectSessionGenerate();

            // NEW: 공통 정보 세팅 (기존 SetupEntitySession 활용)
            SetupEntitySession(newSession, info, serialNumber);

            // NEW: 원래 위치로 정확히 설정 (랜덤 위치 없음)
            newSession.PosX = originalX;
            newSession.PosY = originalY;
            newSession.PosZ = originalZ;

            // NEW: 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // NEW: 자동 공격 엔티티 애니메이션 초기화
            if (serialNumber.StartsWith("O"))
            {
                ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
            }

            // NEW: 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);

            Console.WriteLine($"[SpawnManager] 오브젝트 {serialNumber} 원래 위치 재생성 완료");
        }

        // 몬스터 재생성 (한 마리만)
        private void RespawnMonster(string serialNumber, GameRoom room)
        {
            // 원본 몬스터 설정 찾기
            if (!_monsterSceneSettings.TryGetValue(room.SceneName, out var monsterSettings))
                return;

            var originalSetting = monsterSettings.FirstOrDefault(s => s.serialNumber == serialNumber);
            if (originalSetting == null)
            {
                Console.WriteLine($"[Error] 몬스터 설정을 찾을 수 없습니다: {serialNumber}");
                return;
            }

            // 몬스터 정보 가져오기
            string key = $"{serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }

            var newSession = SessionManager.Instance.MonsterSessionGenerate();

            // 공통 정보 세팅
            SetupEntitySession(newSession, info, originalSetting.serialNumber);

            // 위치 정보 세팅 (랜덤 위치)
            Vector3 spawnPos = Extension.ParseVector3(originalSetting.makePos);
            if (float.TryParse(originalSetting.makeRadius, out float radius))
            {
                Random rand = new Random();
                double angle = rand.NextDouble() * 2 * Math.PI;
                double r = radius * Math.Sqrt(rand.NextDouble());

                newSession.PosX = spawnPos.X + (float)(r * Math.Cos(angle));
                newSession.PosY = spawnPos.Y;
                newSession.PosZ = spawnPos.Z + (float)(r * Math.Sin(angle));
            }

            // 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // NEW: 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);
            
            // NEW: ScheduleManager에 자동 공격 엔티티 애니메이션 초기화 (재생성 몬스터)
            if (serialNumber.StartsWith("M")) // 모든 몬스터
            {
                ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
            }

            Console.WriteLine($"[SpawnManager] 몬스터 {serialNumber} 재생성 완료");
        }

        /// <summary>
        /// NEW: 특정 위치에서 몬스터 재생성 - 원래 위치 복원
        /// - 사망한 몬스터의 정확한 위치에서 재생성
        /// - 다중 스폰 포인트 문제 해결 (M000 등)
        /// </summary>
        private void RespawnMonsterAtPosition(string serialNumber, GameRoom room, float originalX, float originalY, float originalZ)
        {
            Console.WriteLine($"[SpawnManager] 몬스터 {serialNumber} 원래 위치에서 재생성: ({originalX}, {originalY}, {originalZ})");

            // NEW: 몬스터 정보 가져오기 (기존 방식과 동일)
            string key = $"{serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }

            // NEW: 새로운 몬스터 세션 생성
            var newSession = SessionManager.Instance.MonsterSessionGenerate();

            // NEW: 공통 정보 세팅 (기존 SetupEntitySession 활용)
            SetupEntitySession(newSession, info, serialNumber);

            // NEW: 원래 위치로 정확히 설정 (랜덤 위치 없음)
            newSession.PosX = originalX;
            newSession.PosY = originalY;
            newSession.PosZ = originalZ;

            // NEW: 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // NEW: 자동 공격 엔티티 애니메이션 초기화 (몬스터)
            if (serialNumber.StartsWith("M"))
            {
                ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);
            }

            // NEW: 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);

            Console.WriteLine($"[SpawnManager] 몬스터 {serialNumber} 원래 위치 재생성 완료");
        }

        /// <summary>
        /// NEW: 몬스터를 원래 스폰 영역에서 랜덤 재생성
        /// - 사망한 몬스터의 위치를 기준으로 가장 가까운 스폰 설정 찾기
        /// - 해당 스폰 설정의 중심점과 반지름을 사용해서 랜덤 재생성
        /// - 자연스럽고 다양한 재생성 위치 제공
        /// </summary>
        private void RespawnMonsterInOriginalSpawnArea(string serialNumber, string sceneName, float originalX, float originalY, float originalZ)
        {
            // NEW: 해당 씬의 룸 찾기
            if (!Program.GameRooms.TryGetValue(sceneName, out var room))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'을 찾을 수 없습니다.");
                return;
            }

            // NEW: 해당 씬의 몬스터 설정들 가져오기
            if (!_monsterSceneSettings.TryGetValue(sceneName, out var monsterSettings))
            {
                Console.WriteLine($"[Error] 씬 '{sceneName}'의 몬스터 설정을 찾을 수 없습니다.");
                return;
            }

            // NEW: 같은 시리얼넘버의 스폰 설정들 중에서 가장 가까운 것 찾기
            var matchingSettings = monsterSettings.Where(s => s.serialNumber == serialNumber).ToList();
            if (!matchingSettings.Any())
            {
                Console.WriteLine($"[Error] 몬스터 설정을 찾을 수 없습니다: {serialNumber}");
                return;
            }

            // NEW: 사망한 위치와 가장 가까운 스폰 설정 찾기
            MonsterSceneSettingData closestSetting = null;
            float minDistance = float.MaxValue;

            foreach (var setting in matchingSettings)
            {
                Vector3 spawnCenter = Extension.ParseVector3(setting.makePos);
                
                // NEW: 거리 계산 (Vector3.Distance 대신 직접 계산)
                float dx = originalX - spawnCenter.X;
                float dy = originalY - spawnCenter.Y;
                float dz = originalZ - spawnCenter.Z;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestSetting = setting;
                }
            }

            if (closestSetting == null)
            {
                Console.WriteLine($"[Error] 가장 가까운 스폰 설정을 찾을 수 없습니다: {serialNumber}");
                return;
            }

            //Console.WriteLine($"[SpawnManager] 몬스터 {serialNumber} - 가장 가까운 스폰 영역에서 재생성 (거리: {minDistance:F2})");

            // NEW: 해당 스폰 설정을 사용해서 랜덤 재생성 (기존 SpawnMonster 방식 활용)
            SpawnSingleMonsterFromSetting(closestSetting, room);
        }

        /// <summary>
        /// NEW: 특정 몬스터 설정으로 몬스터 1마리만 생성
        /// - 기존 SpawnMonster 로직을 활용하되 1마리만 생성
        /// - 랜덤 위치 생성으로 자연스러운 재생성
        /// </summary>
        private void SpawnSingleMonsterFromSetting(MonsterSceneSettingData setting, GameRoom room)
        {
            // NEW: 몬스터 정보 가져오기
            string key = $"{setting.serialNumber}_1";
            if (!_characterInfos.TryGetValue(key, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber+Level '{key}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }

            // NEW: 새로운 몬스터 세션 생성
            var newSession = SessionManager.Instance.MonsterSessionGenerate();

            // NEW: 공통 정보 세팅
            SetupEntitySession(newSession, info, setting.serialNumber);

            // NEW: 랜덤 위치 생성 (고유 시드 사용으로 겹치지 않는 랜덤 값 보장)
            Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
            if (float.TryParse(setting.makeRadius, out float radius))
            {
                // 현재 시간 + 세션ID로 고유한 시드 생성
                Random rand = new Random((int)(DateTime.UtcNow.Ticks + newSession.SessionId));
                double angle = rand.NextDouble() * 2 * Math.PI;
                double r = radius * Math.Sqrt(rand.NextDouble());

                newSession.PosX = spawnPos.X + (float)(r * Math.Cos(angle));
                newSession.PosY = spawnPos.Y;
                newSession.PosZ = spawnPos.Z + (float)(r * Math.Sin(angle));
            }
            else
            {
                // NEW: 반지름 파싱 실패 시 중심점에서 생성
                newSession.PosX = spawnPos.X;
                newSession.PosY = spawnPos.Y;
                newSession.PosZ = spawnPos.Z;
            }

            // NEW: 룸에 추가 및 연결
            newSession.Room = room;
            room._commonSessions.Add(newSession);
            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));

            // NEW: 자동 공격 엔티티 애니메이션 초기화
            ScheduleManager.Instance.SetAnimationState(newSession, Define.Anime.Idle);

            // NEW: 재생성 알림 브로드캐스트
            BroadcastEntityEnter(newSession, room);

            //Console.WriteLine($"[SpawnManager] 몬스터 {setting.serialNumber} 랜덤 위치 재생성 완료: ({newSession.PosX:F2}, {newSession.PosY:F2}, {newSession.PosZ:F2})");
        }

        /// <summary>
        /// NEW: 엔티티 정보 설정 - 파이어베이스 데이터 기반 세션 초기화
        /// - 파이어베이스에서 변환된 애니메이션 데이터 직접 설정 (하드코딩 제거)
        /// - 재생성 시 기본 상태로 초기화
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