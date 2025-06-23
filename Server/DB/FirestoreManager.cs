using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Newtonsoft.Json;
using Server;

#region 데이터 클래스 정의
[Serializable]
public class CharacterInfoData
{
    public string serialNumber;
    public string level;	
    public string nickName;
    public string needEXP;
    public string dropExp;
    public string invincibility;
    public string maxHp;
    public string body_Size;
    public string moveSpeed;
    public string findRadius;
    public string normalAttackDamage;
    public string normalAttackRange;
    public string hitLength;
}
[Serializable]
public class AttackInfoData
{
    public string attackSerial;
    public string ownerSerial;
    public string name;
    public string type;
    public string range;
    public string animeLength;
    public string attackTiming;
    public string damageMultiplier;
    public string coolTime;
    public string effectSerial;
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
public class CharacterInfoList
{ public List<CharacterInfoData> characterInfos; }
[Serializable]
public class AttackInfoList
{ public List<AttackInfoData> attackInfos; }
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

public class FirestoreManager
{
    // 파이어스토어 데이터베이스    
    private FirestoreDb firestore;
    
    // NetworkRoomSceneData Dictionary (씬 변경 시 스폰 위치 찾기용)
    private Dictionary<string, NetworkRoomSceneData> _networkRoomSceneDict = new Dictionary<string, NetworkRoomSceneData>();

    // 생성자
    public FirestoreManager()
    {
        // 환경 변수로 인증 정보 지정
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", @"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Firebase\d-rpg-server-firebase-adminsdk-fbsvc-cc3363d61c.json");
        firestore = FirestoreDb.Create("d-rpg-server");
    }
    
    // 초기화
    public async Task Init()
    {
        // 병렬 실행
        var tasks = new List<Task>
        {
            LoadAndSaveCollectionToJson<CharacterInfoData,        CharacterInfoList>      ("characterInfos",          "CharacterInfoData.json"),
            LoadAndSaveCollectionToJson<AttackInfoData,           AttackInfoList>         ("attackInfos",             "AttackInfoData.json"),
            LoadAndSaveCollectionToJson<ObjectSceneSettingData,   ObjectSceneSettingList> ("objectSceneSettingInfos", "ObjectSceneSettingData.json"),
            LoadAndSaveCollectionToJson<MonsterSceneSettingData,  MonsterSceneSettingList>("monsterSceneSettingInfos","MonsterSceneSettingData.json"),
            LoadAndSaveCollectionToJson<NetworkRoomSceneData,     NetworkRoomSceneList>   ("networkRoomSceneInfos",   "NetworkRoomSceneData.json"),
        };
        await Task.WhenAll(tasks);
        
        // 룸 세팅
        await CreateGameRoomsBasedOnSceneData();
        
        // 씬 오브젝트 + 몬스터 초기 세팅 정보를 불러오고, 스폰 매니저를 이용해서, 생성 진행...
        SpawnManagerInit();
        
        // NetworkRoomSceneData Dictionary 생성
        CreateNetworkRoomSceneDict();
        
        Console.WriteLine("DB 세팅 완료!");
    }
    
    // 파이어스토리지 최신화
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

        // Console.WriteLine($"✅ Firestore '{collectionName}' → JSON 저장 완료: {path}");
    }
    
    // 룸 세팅 
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
                            Program.GameRooms.Add(sceneData.fromScene, new GameRoom(sceneData.fromScene, Program.DBManager));
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

    // 스폰 세팅
    private void SpawnManagerInit()
    {
        // -------------------------------------------------- 스폰 데이터 로드 및 SpawnManager 초기화 --------------------------------------------------
        // 엔티티 공통 정보
        string characterInfoPath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "CharacterInfoData.json");
        string characterInfoJson = File.ReadAllText(characterInfoPath);
        CharacterInfoList characterInfoList = JsonConvert.DeserializeObject<CharacterInfoList>(characterInfoJson);

        // 오브젝트 씬 정보
        string objectScenePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "ObjectSceneSettingData.json");
        string objectSceneJson = File.ReadAllText(objectScenePath);
        ObjectSceneSettingList objectSceneList = JsonConvert.DeserializeObject<ObjectSceneSettingList>(objectSceneJson);

        // 몬스터 씬 정보
        string monsterScenePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "MonsterSceneSettingData.json");
        string monsterSceneJson = File.ReadAllText(monsterScenePath);
        MonsterSceneSettingList monsterSceneList = JsonConvert.DeserializeObject<MonsterSceneSettingList>(monsterSceneJson);

        // 애니메이션 데이터 변환 (문자열 → 리스트)
        // ConvertAnimationDataToLists(characterInfoList.characterInfos);
        
        // SpawnManager에 로드한 데이터를 전달하여 초기화
        SpawnManager.Instance.Init(characterInfoList.characterInfos, objectSceneList.objectSceneSettingInfos, monsterSceneList.monsterSceneSettingInfos);
    }

    // NetworkRoomSceneData Dictionary 생성
    private void CreateNetworkRoomSceneDict()
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
                    if (!string.IsNullOrEmpty(sceneData.fromScene) && !string.IsNullOrEmpty(sceneData.toScene))
                    {
                        // Unity 클라이언트와 동일한 키 형식: "fromScene_toScene"
                        string compoundKey = $"{sceneData.fromScene}_{sceneData.toScene}";
                        if (!_networkRoomSceneDict.ContainsKey(compoundKey))
                        {
                            _networkRoomSceneDict.Add(compoundKey, sceneData);
                            //Console.WriteLine($"씬 변경 데이터 추가: {compoundKey} -> {sceneData.spawnPosition}");
                        }
                    }
                }
                //Console.WriteLine($"NetworkRoomSceneData Dictionary 생성 완료: {_networkRoomSceneDict.Count}개 항목");
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
    }
    
    // 씬 변경 시 스폰 위치 찾기 (Unity 클라이언트와 동일한 로직)
    public (float x, float y, float z) GetSpawnPosition(string fromScene, string toScene)
    {
        string compoundKey = $"{fromScene}_{toScene}";
        
        if (_networkRoomSceneDict.ContainsKey(compoundKey) && _networkRoomSceneDict[compoundKey] != null)
        {
            var spawnPositionString = _networkRoomSceneDict[compoundKey].spawnPosition;
            Vector3 posVec = Extension.ParseVector3(spawnPositionString);
            return (posVec.X, posVec.Y, posVec.Z);
        }
        else
        {
            Console.WriteLine($"스폰 위치를 찾을 수 없음: {compoundKey}");
            return (0f, 0f, 0f); // 기본값
        }
    }
}