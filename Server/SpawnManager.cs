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

        private Dictionary<string, CharacterInfoData>      _characterInfos          = new Dictionary<string, CharacterInfoData>();       // 플레이어/몬스터/오브젝트 정보
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
        
        // 서버가 켜지고, 1회 세팅
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
            if (int.TryParse(info.normalAttackDamage, out int damage))
                newSession.Damage = damage;
            
            // 위치 정보 세팅(고정)
            Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
            newSession.PosX = spawnPos.X;
            newSession.PosY = spawnPos.Y;
            newSession.PosZ = spawnPos.Z;

            // 룸에 넣어주기
            newSession.Room = room;
            room._commonSessions.Add(newSession);

            newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));
        }
        
        // 서버가 켜지고, 1회 세팅
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

            Random rand = new Random();
            
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

                // 위치 정보 세팅(중심값 + 범위 안에서 랜덤값을 더해줌)
                Vector3 spawnPos = Extension.ParseVector3(setting.makePos);
                if (float.TryParse(setting.makeRadius, out float radius))
                {
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
            }
        }
    }
} 