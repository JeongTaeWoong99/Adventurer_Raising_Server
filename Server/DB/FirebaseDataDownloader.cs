using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Newtonsoft.Json;
using Server;

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
public class MonsterAndObjectInfoData   // 내용이 같기 때문에, 공통 사용
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
public class MonsterAndObjectList
{ public List<MonsterAndObjectInfoData> monsterAndObjectInfos; }
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
        // 🔸 컬렉션 별 비동기 JSON 저장
        await LoadAndSaveCollectionToJson<PlayerInfoData,           PlayerLevelInfoList>    ("playerLevelInfos",        "PlayerLevelInfoData.json");
        await LoadAndSaveCollectionToJson<MonsterAndObjectInfoData, MonsterAndObjectList>   ("monsterInfos",            "MonsterInfoData.json");
        await LoadAndSaveCollectionToJson<MonsterAndObjectInfoData, MonsterAndObjectList>   ("objectInfos",             "ObjectInfoData.json");
        await LoadAndSaveCollectionToJson<MonsterSceneSettingData,  MonsterSceneSettingList>("monsterSceneSettingInfos","MonsterSceneSettingData.json");
        await LoadAndSaveCollectionToJson<ObjectSceneSettingData,   ObjectSceneSettingList> ("objectSceneSettingInfos", "ObjectSceneSettingData.json");
        await LoadAndSaveCollectionToJson<NetworkRoomSceneData,     NetworkRoomSceneList>   ("networkRoomSceneInfos",   "NetworkRoomSceneData.json");
        
        CreateGameRoomsBasedOnSceneData();
        
        Console.WriteLine("DB 세팅 완료!");
    }

    private void CreateGameRoomsBasedOnSceneData()
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
}