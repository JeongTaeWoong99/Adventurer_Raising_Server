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

        private Dictionary<string, ObjectAndMonsterInfoData>      _entityInfos          = new Dictionary<string, ObjectAndMonsterInfoData>();       // 오브젝트 + 몬스터 정보
        private Dictionary<string, List<ObjectSceneSettingData>>  _objectSceneSettings  = new Dictionary<string, List<ObjectSceneSettingData>>();   // 오브젝트 씬 세팅 정보 
        private Dictionary<string, List<MonsterSceneSettingData>> _monsterSceneSettings = new Dictionary<string, List<MonsterSceneSettingData>>();  // 몬스터 씬 세팅 정보
        
        // 파이어 베이스에서 받아온 정보로 정보 세팅
        public void Init(List<ObjectAndMonsterInfoData> entityInfos, List<ObjectSceneSettingData> objectSettings, List<MonsterSceneSettingData> monsterSettings)
        {
            // 각종 스폰 관련 데이터를 미리 Dictionary로 만들어 빠르게 조회할 수 있도록 저장합니다.
            _entityInfos = entityInfos.ToDictionary(info => info.serialNumber);
            
            _objectSceneSettings = objectSettings
                .GroupBy(setting => setting.sceneName)
                .ToDictionary(group => group.Key, group => group.ToList());
            
            _monsterSceneSettings = monsterSettings
                .GroupBy(setting => setting.sceneName)
                .ToDictionary(group => group.Key, group => group.ToList());
        }
        
        // 서버가 켜지고, 1회 세팅
        public void SpawnAllEntities()
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
            Console.WriteLine("모든 씬의 몬스터 및 오브젝트 스폰 완료!");
        }
        
        // 서버가 켜지고, 1회 세팅
        private void SpawnObject(ObjectSceneSettingData setting, GameRoom room)
        {
            if (!_entityInfos.TryGetValue(setting.serialNumber, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber '{setting.serialNumber}'에 해당하는 ObjectInfo를 찾을 수 없습니다.");
                return;
            }

            var newSession = SessionManager.Instance.ObjectSessionGenerate();
            
            // 공통 정보 세팅
            newSession.serialNumber = setting.serialNumber;
            newSession.nickname = info.nickName;
            if (int.TryParse(info.maxHp, out int maxHp))
                newSession.currentHP = maxHp;
            if (bool.TryParse(info.invincibility, out bool invincibility))
                newSession.Invincibility = invincibility;
            
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
            
            if (!_entityInfos.TryGetValue(setting.serialNumber, out var info))
            {
                Console.WriteLine($"[Error] SerialNumber '{setting.serialNumber}'에 해당하는 MonsterInfo를 찾을 수 없습니다.");
                return;
            }
             
            if (!int.TryParse(setting.spawnNumber, out int spawnCount))
                return;

            Random rand = new Random();
            
            for (int i = 0; i < spawnCount; i++)
            {
                var newSession = SessionManager.Instance.MonsterSessionGenerate();

                // 공통 정보 세팅
                newSession.serialNumber = setting.serialNumber;
                newSession.nickname = info.nickName;
                if (int.TryParse(info.maxHp, out int maxHp))
                    newSession.currentHP = maxHp;
                if (bool.TryParse(info.invincibility, out bool invincibility))
                    newSession.Invincibility = invincibility;

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