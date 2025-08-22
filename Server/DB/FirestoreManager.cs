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
    public string type;
    public string name;
    public string colliderType;
    public string fixedCreatePos;
    public string createPos;
    public string range;
    public string animeLength;
    public string attackTiming;
    public string repeat;
    public string damageMultiplier;
    public string moveSpeed;
    public string duration;
    public string buffType;
    public string coolTime;
    public string penetration;
    public string attackEffectSerial;
    public string hitEffectSerial;
    public string image;
}
[Serializable]
public class MonsterSceneSettingData
{
    public string mmNumber;      // NEW: 관리번호 (M01, M02, ...)
    public string sceneName;
    public string serialNumber;
    public string spawnNumber;
    public string makePos;
    public string makeRadius;
}
[Serializable]
public class ObjectSceneSettingData
{
    public string mmNumber;      // NEW: 관리번호 (O01, O02, ...)
    public string sceneName;
    public string serialNumber;
    public string makePos;
}
[Serializable]
public class NetworkRoomSceneData
{
    public string mmNumber;      // 관리번호 (N00, N01, N02, ...)
    public string type;          // loginToSave, forcedMove, portal
    public string toScene;       // 이동할 씬 이름
    public string spawnPosition; // 스폰 위치
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
    
    // mmNumber 기반 빠른 검색용 딕셔너리
    private Dictionary<string, NetworkRoomSceneData> _sceneMmNumberDict = new Dictionary<string, NetworkRoomSceneData>();

    // 생성자
    public FirestoreManager()
    {
        try
        {
            // 기존 환경 변수 초기화
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", null);
            
            // 환경 변수에서 먼저 확인
            string credentialsPath = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_PATH");
            
            if (string.IsNullOrEmpty(credentialsPath))
            {
                // 개발 환경용 상대 경로
                credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Firebase", "d-rpg-server-24c1ffc47d4a.json");
                credentialsPath = Path.GetFullPath(credentialsPath);
            }
            
            // 파일 존재 여부 확인
            if (!File.Exists(credentialsPath))
            {
                Console.WriteLine($"⚠️ Firebase 인증 파일을 찾을 수 없습니다: {credentialsPath}");
                Console.WriteLine("기존 JSON 파일을 사용하여 진행합니다.");
                return;
            }
            
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            
            Console.WriteLine($"✅ Firebase 인증 파일 설정 완료");
            Console.WriteLine($"🔧 환경 변수 확인: {Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS") != null}");
            
            // 파일 내용 확인
            string fileContent = File.ReadAllText(credentialsPath);
            if (fileContent.Contains("d-rpg-server"))
            {
                Console.WriteLine("인증 파일 내용 확인 완료");
            }
            else
            {
                Console.WriteLine("⚠️ 인증 파일 내용이 올바르지 않을 수 있습니다");
            }
            
            // FirestoreDb 초기화
            firestore = FirestoreDb.Create("d-rpg-server");
            Console.WriteLine("Firestore 데이터베이스 연결 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FirestoreManager 초기화 실패: {ex.Message}");
            Console.WriteLine($"상세 오류: {ex.StackTrace}");
            firestore = null;
        }
    }
    
    // 초기화
    public async Task Init()
    {
        try
        {
            Console.WriteLine("Firestore에서 데이터를 가져오는 중...");
            
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
            
            Console.WriteLine("✅ Firestore 데이터 동기화 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Firestore 접근 실패: {ex.Message}");
            Console.WriteLine("기존 JSON 파일이 있다면 그것을 사용하여 계속 진행합니다.");
            
            // 기존 파일 존재 여부 확인
            string dataDir = @"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data";
            if (Directory.Exists(dataDir))
            {
                var files = Directory.GetFiles(dataDir, "*.json");
                Console.WriteLine($"📁 {files.Length}개의 기존 JSON 파일을 찾았습니다.");
                foreach (var file in files)
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)}");
                }
            }
            else
            {
                Console.WriteLine("⚠️ Data 폴더가 없습니다. 서버가 제대로 작동하지 않을 수 있습니다.");
            }
        }
        
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
        try
        {
            Console.WriteLine($"🔍 '{collectionName}' 컬렉션 접근 시도...");
            
            CollectionReference colRef = firestore.Collection(collectionName);
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();

            Console.WriteLine($"📊 '{collectionName}' 문서 개수: {snapshot.Documents.Count}");

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
            object wrapper = new TList();
            var listField = typeof(TList).GetFields()[0];
            listField.SetValue(wrapper, items);

            string jsonData = JsonConvert.SerializeObject(wrapper, Formatting.Indented);

            // 저장 경로
            string saveDir = @"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data";
            string path = Path.Combine(saveDir, outputFileName);
            Directory.CreateDirectory(saveDir);

            File.WriteAllText(path, jsonData);

            Console.WriteLine($"✅ Firestore '{collectionName}' → JSON 저장 완료: {Path.GetFileName(outputFileName)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ '{collectionName}' 접근 실패: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   상세 오류: {ex.InnerException.Message}");
            }
            throw;
        }
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
                HashSet<string> createdRooms = new HashSet<string>();
                
                foreach (NetworkRoomSceneData sceneData in sceneListData.networkRoomSceneInfos)
                {
                    if (!string.IsNullOrEmpty(sceneData.toScene) && !createdRooms.Contains(sceneData.toScene))
                    {
                        // SavedScene은 동적이므로 룸을 미리 만들지 않음
                        if (sceneData.toScene == "SavedScene")
                        {
                            //Console.WriteLine("SavedScene은 동적 룸이므로 생성하지 않음");
                            continue;
                        }
                        
                        Program.GameRooms.Add(sceneData.toScene, new GameRoom(sceneData.toScene, Program.DBManager));
                        createdRooms.Add(sceneData.toScene);
                        //Console.WriteLine($"GameRoom 생성: {sceneData.toScene}");
                    }
                }
                
                //Console.WriteLine($"총 {createdRooms.Count}개의 GameRoom 생성 완료");
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
                    // mmNumber 기반 딕셔너리만 사용
                    if (!string.IsNullOrEmpty(sceneData.mmNumber))
                    {
                        if (!_sceneMmNumberDict.ContainsKey(sceneData.mmNumber))
                        {
                            _sceneMmNumberDict.Add(sceneData.mmNumber, sceneData);
                            //Console.WriteLine($"mmNumber 씬 변경 데이터 추가: {sceneData.mmNumber} ({sceneData.type}) -> {sceneData.toScene}");
                        }
                    }
                }
                //Console.WriteLine($"mmNumber 기반 Dictionary 생성 완료: {_sceneMmNumberDict.Count}개 항목");
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
    
    /// <summary>
    /// mmNumber 기반 빠른 스폰 위치 검색
    /// - O(1) 시간복잡도로 즉시 스폰 위치 반환
    /// - type을 통해 이동 방식 구분 (loginToSave, forcedMove, portal)
    /// </summary>
    public (float x, float y, float z, string type, string toScene) GetSpawnPositionByMmNumber(string mmNumber)
    {
        if (_sceneMmNumberDict.TryGetValue(mmNumber, out NetworkRoomSceneData sceneData))
        {
            Vector3 posVec = Extension.ParseVector3(sceneData.spawnPosition);
            return (posVec.X, posVec.Y, posVec.Z, sceneData.type, sceneData.toScene);
        }
        else
        {
            Console.WriteLine($"mmNumber로 스폰 위치를 찾을 수 없음: {mmNumber}");
            return (0f, 0f, 0f, "", ""); // 기본값
        }
    }
}