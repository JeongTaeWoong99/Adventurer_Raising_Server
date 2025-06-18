using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System;
namespace Server.DB;

public class DBManager
{
	public AuthManager      _auth;
	public RealTimeManager  _realTime;
	public FirestoreManager _firestore;	// App 인스턴스 분리 및 초기화 필요함...

	// 데이터 Dictionary들
	private Dictionary<string, CharacterInfoData> CharacterInfoDict { get;  set; } = new Dictionary<string, CharacterInfoData>();
	private Dictionary<string, AttackInfoData>    AttackInfoDict    { get;  set; } = new Dictionary<string, AttackInfoData>();

	public async Task Init()
	{
		// 생성 및 파이어베이스 설정
		_auth      = new AuthManager();	
		_realTime  = new RealTimeManager();	
		_firestore = new FirestoreManager();
		
		// 비동기 초기화 작업 진행
		await _firestore.Init(); // 파이어스토어 데이터 최신화 + 룸 세팅 + 스폰 세팅
		
		// JSON 데이터 로드 후, 디렉토리에 등록
		LoadCharacterInfoData();
		LoadAttackInfoData();
	}
	
	// CharacterInfoData 로드
	private void LoadCharacterInfoData()
	{
		try
		{
			string filePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "CharacterInfoData.json");
			if (File.Exists(filePath))
			{
				string json = File.ReadAllText(filePath);
				var characterInfoList = JsonConvert.DeserializeObject<CharacterInfoList>(json);
				
				if (characterInfoList?.characterInfos != null)
				{
					foreach (var characterInfo in characterInfoList.characterInfos)
					{
						string key = $"{characterInfo.serialNumber}_{characterInfo.level}";
						CharacterInfoDict[key] = characterInfo;
					}
					//Console.WriteLine($"캐릭터 정보 데이터 로드 완료: {CharacterInfoDict.Count}개");
				}
			}
			else
			{
				Console.WriteLine("CharacterInfoData.json 파일을 찾을 수 없습니다.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"캐릭터 정보 데이터 로드 실패: {ex.Message}");
		}
	}
	
	// AttackInfoData 로드
	private void LoadAttackInfoData()
	{
		try
		{
			string filePath = Path.Combine(@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Data", "AttackInfoData.json");
			if (File.Exists(filePath))
			{
				string json = File.ReadAllText(filePath);
				var attackInfoList = JsonConvert.DeserializeObject<AttackInfoList>(json);
				
				if (attackInfoList?.attackInfos != null)
				{
					foreach (var attackInfo in attackInfoList.attackInfos)
					{
						AttackInfoDict[attackInfo.attackSerial] = attackInfo;
					}
					//Console.WriteLine($"공격 정보 데이터 로드 완료: {AttackInfoDict.Count}개");
				}
			}
			else
			{
				Console.WriteLine("AttackInfoData.json 파일을 찾을 수 없습니다.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"공격 정보 데이터 로드 실패: {ex.Message}");
		}
	}
	
	// 공격 정보 가져오기
	public AttackInfoData GetAttackInfo(string attackSerial)
	{
		AttackInfoDict.TryGetValue(attackSerial, out AttackInfoData attackInfo);
		return attackInfo;
	}
	
	// 캐릭터 정보 가져오기
	public CharacterInfoData GetCharacterInfo(string serialNumber, int level)
	{
		string key = $"{serialNumber}_{level}";
		CharacterInfoDict.TryGetValue(key, out CharacterInfoData characterInfo);
		return characterInfo;
	}
}