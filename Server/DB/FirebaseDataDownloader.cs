using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Newtonsoft.Json;
using Server;
using System.Net;
using System.Numerics;
using System.Linq;

#region 데이터 클래스 정의
[Serializable]
public class PlayerInfoData
{
    // public string nickName; => 리얼타임 베이터베이스에서 닉네임으로 사용
    public string maxHp;
    public string attack;
    public string moveSpeed;
    public string b_Type;
    public string b_Size;
    public string ab_Size;
    
    public string level;
    public string needEXP;
}
[Serializable]
public class ObjectAndMonsterInfoData   // 내용이 같기 때문에, 공통 사용
{
    public string nickName;
    public string maxHp;
    public string attack;
    public string moveSpeed;
    public string b_Type;
    public string b_Size;
    public string ab_Size;
    
    public string serialNumber;  
    public string dropExp;       
    public string invincibility;    // 특정 오브젝트는 무적 
    public string find_Radius;   
    public string attack1_Length;
    public string attack1_Timing;
}
[Serializable]
public class MonsterSceneSettingData
{
    public string sceneName;
    public string serialNumber;
    public string spawnNumber;
    public string makePos;
    public string makeRadius;
}
[Serializable]
public class ObjectSceneSettingData
{
    public string sceneName;
    public string serialNumber;
    public string makePos;
}
[Serializable]
public class NetworkRoomSceneData
{
    // fromScene이름으로 네트워크 Room을 만듬. ※ 단, Unknown은 제외.
    public string fromScene;
    public string toScene;       
    public string spawnPosition; 
}
[Serializable]
public class PlayerLevelInfoList
{ public List<PlayerInfoData> playerLevelInfos; }
[Serializable]
public class ObjectAndMonsterList
{ public List<ObjectAndMonsterInfoData> objectAndMonsterInfos; }
[Serializable]
public class MonsterSceneSettingList
{ public List<MonsterSceneSettingData> monsterSceneSettingInfos; }
[Serializable]
public class ObjectSceneSettingList
{ public List<ObjectSceneSettingData> objectSceneSettingInfos; }
[Serializable]
public class  NetworkRoomSceneList
{ public List<NetworkRoomSceneData>  networkRoomSceneInfos; }

#endregion

public class FirebaseDataDownloader
{
    private FirestoreDb firestore;

    public FirebaseDataDownloader(string projectId, string jsonPath)
    {
        // 환경 변수로 인증 정보 지정
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", jsonPath);
        firestore = FirestoreDb.Create(projectId);
    }

    public async Task DownloadAllAsync()
    {
        // 병렬 실행
        var tasks = new List<Task>
        {
            // 최신 정보 받아오기
            LoadAndSaveCollectionToJson<PlayerInfoData,           PlayerLevelInfoList>    ("playerLevelInfos",        "PlayerLevelInfoData.json"),
            LoadAndSaveCollectionToJson<ObjectAndMonsterInfoData, ObjectAndMonsterList>   ("objectAndMonsterInfos",   "ObjectAndMonsterInfoData.json"),
            LoadAndSaveCollectionToJson<ObjectSceneSettingData,   ObjectSceneSettingList> ("objectSceneSettingInfos", "ObjectSceneSettingData.json"),
            LoadAndSaveCollectionToJson<MonsterSceneSettingData,  MonsterSceneSettingList>("monsterSceneSettingInfos","MonsterSceneSettingData.json"),
            LoadAndSaveCollectionToJson<NetworkRoomSceneData,     NetworkRoomSceneList>   ("networkRoomSceneInfos",   "NetworkRoomSceneData.json"),
        };
        await Task.WhenAll(tasks);
        
        // 최신 정보를 바탕으로, Room 만들어주기
        await CreateGameRoomsBasedOnSceneData();
            
        // 최신 정보를 바탕으로, Room에 오브젝트 세팅
        await CreateObjectAndMonsterBasedOnSceneData();
        
        Console.WriteLine("DB 세팅 완료!");
    }
    
    private async Task LoadAndSaveCollectionToJson<TItem, TList>(string collectionName, string outputFileName) where TItem : new() where TList : new()
    {
        CollectionReference colRef   = firestore.Collection(collectionName);
        QuerySnapshot       snapshot = await colRef.GetSnapshotAsync();

        //Console.WriteLine($"[DEBUG] {collectionName} 문서 개수: {snapshot.Documents.Count}");

        List<TItem> items = new List<TItem>();
        foreach (DocumentSnapshot doc in snapshot.Documents)
        {
            if (!doc.Exists)
                continue;

            Dictionary<string, object> data = doc.ToDictionary();
            //Console.WriteLine("[DEBUG] 문서 데이터: " + string.Join(", ", data.Keys));

            TItem item = new TItem();

            foreach (var kv in data)
            {
                string fieldName = kv.Key;
                object fieldValue = kv.Value;

                // 필드 매핑
                var field = typeof(TItem).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null && fieldValue != null)
                {
                    string val = fieldValue.ToString();
                    field.SetValue(item, val);
                    //Console.WriteLine($"[필드 매핑] {fieldName} → {val}");
                    continue;
                }

                // 프로퍼티 매핑
                var prop = typeof(TItem).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite && fieldValue != null)
                {
                    string val = fieldValue.ToString();
                    prop.SetValue(item, val);
                    //Console.WriteLine($"[프로퍼티 매핑] {fieldName} → {val}");
                    continue;
                }

                //Console.WriteLine($"⚠️ 매핑 실패: {fieldName}");
            }

            items.Add(item);
        }

        // JSON 구조 래핑
        object wrapper   = new TList();
        var    listField = typeof(TList).GetFields()[0];
        listField.SetValue(wrapper, items);

        string jsonData = JsonConvert.SerializeObject(wrapper, Formatting.Indented);

        // 저장 경로
        string saveDir = @"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data";
        string path    = Path.Combine(saveDir, outputFileName);
        Directory.CreateDirectory(saveDir);

        File.WriteAllText(path, jsonData);

        Console.WriteLine($"✅ Firestore '{collectionName}' → JSON 저장 완료: {path}");
    }
    
    private Task CreateGameRoomsBasedOnSceneData()
    {
        string filePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "NetworkRoomSceneData.json"); 
        if (File.Exists(filePath))
        {
            string jsonData = File.ReadAllText(filePath);
            NetworkRoomSceneList sceneListData = JsonConvert.DeserializeObject<NetworkRoomSceneList>(jsonData);

            if (sceneListData != null && sceneListData.networkRoomSceneInfos != null)
            {
                foreach (NetworkRoomSceneData sceneData in sceneListData.networkRoomSceneInfos)
                {
                    
                    if (!string.IsNullOrEmpty(sceneData.fromScene) && !Program.GameRooms.ContainsKey(sceneData.fromScene))
                    {
                        // Unknown은 제외.
                        if (sceneData.fromScene.Equals("UnKnown"))
                        {
                            //Console.WriteLine("UnKnown GameRoom 생성 제외");
                        }
                        else
                        {
                            Program.GameRooms.Add(sceneData.fromScene, new GameRoom(sceneData.fromScene));
                            //Console.WriteLine($"GameRoom 생성 : {sceneData.fromScene}");
                        }
                    }
                    else if (Program.GameRooms.ContainsKey(sceneData.fromScene))
                    {
                        //Console.WriteLine($"이미 '{sceneData.fromScene}' 이름의 GameRoom이 존재합니다.");
                    }
                    else if (string.IsNullOrEmpty(sceneData.fromScene))
                    {
                        //Console.WriteLine($"sceneName이 비어있는 NetworkRoomSceneData 항목이 있습니다.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Error: NetworkRoomSceneData.json 파일 내용이 올바르지 않거나 비어있습니다. ({filePath})");
            }
        }
        else
        {
            Console.WriteLine($"Error: NetworkRoomSceneData.json 파일을 찾을 수 없습니다. 경로: {filePath}");
        }

        return Task.CompletedTask;
    }

    private Task CreateObjectAndMonsterBasedOnSceneData()
    {
        // -------------------------------------------------- 정보 데이터 로드 --------------------------------------------------
        // 오브젝트 + 몬스터 정보
        string objectAndMonsterInfoDataFilePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "ObjectAndMonsterInfoData.json");
        string objectAndMonsterDataJsonData = File.ReadAllText(objectAndMonsterInfoDataFilePath);
        ObjectAndMonsterList objectAndObjectAndMonsterDataList = JsonConvert.DeserializeObject<ObjectAndMonsterList>(objectAndMonsterDataJsonData);
        Dictionary<string, ObjectAndMonsterInfoData> objectAndMonsterInfoDict = objectAndObjectAndMonsterDataList.objectAndMonsterInfos.ToDictionary(info => info.serialNumber);

        // 몬스터 정보
        // string monsterInfoDataFilePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "MonsterInfoData.json");
        // string monsterDataJsonData = File.ReadAllText(monsterInfoDataFilePath);
        // MonsterAndObjectList monsterDataList = JsonConvert.DeserializeObject<MonsterAndObjectList>(monsterDataJsonData);
        // Dictionary<string, ObjectAndMonsterInfoData> monsterInfoDict = monsterDataList.monsterAndObjectInfos.ToDictionary(info => info.serialNumber);
        
        // -------------------------------------------------- 오브젝트 생성 --------------------------------------------------
        string objectFilePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "ObjectSceneSettingData.json");
        if (File.Exists(objectFilePath))
        {
            string objectJsonData = File.ReadAllText(objectFilePath);
            ObjectSceneSettingList objectSceneListData = JsonConvert.DeserializeObject<ObjectSceneSettingList>(objectJsonData);

            if (objectSceneListData != null && objectSceneListData.objectSceneSettingInfos != null)
            {
                foreach (ObjectSceneSettingData objectData in objectSceneListData.objectSceneSettingInfos)
                {
                    if (Program.GameRooms.TryGetValue(objectData.sceneName, out GameRoom room)) // 생성 방 찾기
                    {
                        // 오브젝트 씬 세팅에 해당하는 정보 세팅
                        var newSession = SessionManager.Instance.ObjectSessionGenerate();   // 클론 생성
                         
                        newSession.serialNumber = objectData.serialNumber;                  // 시리얼넘버 세팅
                        
                        Vector3 spawnPos = Extension.ParseVector3(objectData.makePos);      // 고정 생성 위치 세팅
                        newSession.PosX = spawnPos.X;                                       
                        newSession.PosY = spawnPos.Y;
                        newSession.PosZ = spawnPos.Z;
                         
                        newSession.Room = room;                                             // 해당 방 세팅
                        room._commonSessions.Add(newSession);                               // 해당 방에 넣어주기
                        
                        // 시리얼 넘버에 해당하는 정보 세팅
                        if (objectAndMonsterInfoDict.TryGetValue(objectData.serialNumber, out ObjectAndMonsterInfoData info))
                        {
                            newSession.nickname = info.nickName;
                            if (int.TryParse(info.maxHp, out int maxHp))
                                newSession.currentHP = maxHp;
                            if (bool.TryParse(info.invincibility, out bool invincibility)) // 무적 상태는 설정해 주어야 한다.
                                newSession.Invincibility = invincibility;
                        }
                        
                        newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777)); // 연결 완료 피드백
                    }
                    else
                    {
                        Console.WriteLine($"Error: Scene '{objectData.sceneName}' not found for object with serial '{objectData.serialNumber}'.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Error: ObjectSceneSettingData.json 파일 내용이 올바르지 않거나 비어있습니다. ({objectFilePath})");
            }
        }
        else
        {
            Console.WriteLine($"Error: ObjectSceneSettingData.json 파일을 찾을 수 없습니다. 경로: {objectFilePath}");
        }
        
        // -------------------------------------------------- 몬스터 생성 --------------------------------------------------
        // string monsterFilePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "MonsterSceneSettingData.json");
        // if (File.Exists(monsterFilePath))
        // {
        //     string monsterJsonData = File.ReadAllText(monsterFilePath);
        //     MonsterSceneSettingList monsterSceneListData = JsonConvert.DeserializeObject<MonsterSceneSettingList>(monsterJsonData);
        //     Random rand = new Random(); // 스폰 랜덤 위치
        //
        //     if (monsterSceneListData != null && monsterSceneListData.monsterSceneSettingInfos != null)
        //     {
        //         foreach (MonsterSceneSettingData monsterData in monsterSceneListData.monsterSceneSettingInfos) 
        //         {
        //             if (Program.GameRooms.TryGetValue(monsterData.sceneName, out GameRoom room)) // 생성 방 찾기
        //             {
        //                 if(int.TryParse(monsterData.spawnNumber, out int spawnCount))   // 스폰 넘버 만큼 반복
        //                 {
        //                     for (int i = 0; i < spawnCount; i++)
        //                     {
        //                         // 몬스터 씬 세팅에 해당하는 정보 세팅
        //                         var newSession = SessionManager.Instance.MonsterSessionGenerate();  // 클론 생성
        //                          
        //                         newSession.serialNumber = monsterData.serialNumber;                 // 시리얼넘버 세팅
        //                         
        //                         Vector3 spawnPos = Extension.ParseVector3(monsterData.makePos);     // 고정 생성 위치 세팅
        //              
        //                         if (float.TryParse(monsterData.makeRadius, out float radius))       // 고정 위치에다가, 랜덤 radius를 더해준다.
        //                         {                                                                   
        //                             double angle = rand.NextDouble() * 2 * Math.PI;
        //                             double r = radius * Math.Sqrt(rand.NextDouble());
        //                              
        //                             newSession.PosX = spawnPos.X + (float)(r * Math.Cos(angle));    // 중심 X + 랜덤 X
        //                             newSession.PosY = spawnPos.Y;                                   // 기본
        //                             newSession.PosZ = spawnPos.Z + (float)(r * Math.Sin(angle));    // 중심 Z + 랜덤 Z
        //                         }
        //                          
        //                         newSession.Room = room;                                             // 해당 방 세팅
        //                         room._commonSessions.Add(newSession);                               // 해당 방에 넣어주기
        //                         
        //                         // 시리얼 넘버에 해당하는 정보 세팅
        //                         if (monsterInfoDict.TryGetValue(monsterData.serialNumber, out MonsterAndObjectInfoData info))
        //                         {
        //                             newSession.nickname = info.nickName;
        //                             if (int.TryParse(info.maxHp, out int hp))
        //                                 newSession.currentHP = hp;
        //                         }
        //                         
        //                         newSession.OnConnected(new IPEndPoint(IPAddress.Loopback, 7777));  // 연결 완료 피드백
        //                     }
        //                 }
        //             }
        //             else
        //                 Console.WriteLine($"Error: Scene '{monsterData.sceneName}' not found for monster with serial '{monsterData.serialNumber}'.");
        //         }
        //     }
        //     else
        //         Console.WriteLine($"Error: MonsterSceneSettingData.json 파일 내용이 올바르지 않거나 비어있습니다. ({monsterFilePath})");
        // }
        // else
        //     Console.WriteLine($"Error: MonsterSceneSettingData.json 파일을 찾을 수 없습니다. 경로: {monsterFilePath}");

        return Task.CompletedTask;
    }
}