using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
namespace Server.DB;

public class DefaultData
{
	public string email;        // 이메일
	public string creationDate; // 요청했을 때 날짜
	
	public string nickname;
	public string serialNumber;
	public string currentLevel;
	public string currentHp;
	public string currentExp;
	public string currentGold;
	
	public string savedScene;		
	public string savedPosition;	
}

public class RealTimeManager
{
	// Firebase Realtime Database 클라이언트
	private FirebaseClient firebaseClient;
	
	// 생성자
	public RealTimeManager()
	{
		string projectId   = "d-rpg-server";
		string firebaseUrl = $"https://{projectId}-default-rtdb.asia-southeast1.firebasedatabase.app/";
		firebaseClient = new FirebaseClient(firebaseUrl);
		// Console.WriteLine($"Firebase Realtime Database 초기화 완료: {firebaseUrl}");
	}
	
	// 기본 캐릭터 데이터 저장 메서드
	public async Task SaveDefaultDataAsync(string email, string nickname, string serialNumber)
	{
		try
		{
			// 전체 이메일을 고유 식별자로 사용하되, Firebase 키로 사용할 수 있도록 문자 치환
			// @ -> _AT_, . -> _DOT_
			string userID = email.Replace("@", "_AT_").Replace(".", "_DOT_");
			
			// 기본 데이터 객체 생성
			DefaultData newDefaultData = new DefaultData();
			
			// 1. 이메일
			newDefaultData.email = email;
			
			// 2. 생성 날짜 저장
			DateTime today = DateTime.Now;
			newDefaultData.creationDate = today.ToString("yyyy-MM-dd");
			
			// 3. 닉네임
			newDefaultData.nickname = nickname;
			
			// 4. 시리얼넘버
			newDefaultData.serialNumber = serialNumber;
			
			// 5. 기본 게임 데이터 설정 (레벨 1 기준)
			// TODO: 나중에 실제 세팅값에서 가져와야 함
			newDefaultData.currentLevel  = "1";
			newDefaultData.currentHp     = "1000";     	// 기본 HP
			newDefaultData.currentExp    = "0";      	// 시작 경험치
			newDefaultData.currentGold   = "0";     	// 시작 골드
			newDefaultData.savedScene    = "UnKnown";   // 기본 씬
			newDefaultData.savedPosition = "UnKnown";   // 기본 위치
			
			// JSON으로 변환하여 Firebase Realtime Database에 저장
			await firebaseClient.Child("Users").Child(userID).PutAsync(newDefaultData);
				
			// Console.WriteLine($"기본 데이터 저장 완료: {userID} (원본 이메일: {email})");
		}
		catch (Exception e)
		{
			// Console.WriteLine($"기본 데이터 저장 실패: {e.Message}");
			throw;
		}
	}
	
	// 사용자 데이터 가져오기 메서드
	public async Task<DefaultData> GetUserDataAsync(string email)
	{
		try
		{
			// 전체 이메일을 고유 식별자로 사용하되, Firebase 키로 사용할 수 있도록 문자 치환
			// @ -> _AT_, . -> _DOT_
			string userID = email.Replace("@", "_AT_").Replace(".", "_DOT_");
			
			// Firebase Realtime Database에서 사용자 데이터 가져오기
			var userData = await firebaseClient.Child("Users").Child(userID).OnceSingleAsync<DefaultData>();
			
			if (userData != null)
			{
				// Console.WriteLine($"사용자 데이터 가져오기 성공: {userID} (원본 이메일: {email})");
				return userData;
			}
			else
			{
				// Console.WriteLine($"사용자 데이터가 존재하지 않음: {userID} (원본 이메일: {email})");
				return null;
			}
		}
		catch (Exception e)
		{
			// Console.WriteLine($"사용자 데이터 가져오기 실패: {e.Message}");
			return null;
		}
	}
	
	// 사용자의 savedScene 업데이트 메서드
	public async Task<bool> UpdateUserSceneAsync(string email, string newScene)
	{
		try
		{
			// 전체 이메일을 고유 식별자로 사용하되, Firebase 키로 사용할 수 있도록 문자 치환
			// @ -> _AT_, . -> _DOT_
			string userID = email.Replace("@", "_AT_").Replace(".", "_DOT_");
			
			// Firebase Realtime Database에서 savedScene 업데이트
			await firebaseClient.Child("Users").Child(userID).Child("savedScene").PutAsync($"\"{newScene}\"");
			
			// Console.WriteLine($"사용자 씬 업데이트 성공: {userID} -> {newScene}");
			return true;
		}
		catch (Exception e)
		{
			// Console.WriteLine($"사용자 씬 업데이트 실패: {e.Message}");
			return false;
		}
	}
	
	// 사용자의 savedPosition 업데이트 메서드
	public async Task<bool> UpdateUserPositionAsync(string email, string newPosition)
	{
		try
		{
			// 전체 이메일을 고유 식별자로 사용하되, Firebase 키로 사용할 수 있도록 문자 치환
			// @ -> _AT_, . -> _DOT_
			string userID = email.Replace("@", "_AT_").Replace(".", "_DOT_");
			
			// Firebase Realtime Database에서 savedPosition 업데이트
			await firebaseClient.Child("Users").Child(userID).Child("savedPosition").PutAsync($"\"{newPosition}\"");
			
			// Console.WriteLine($"사용자 위치 업데이트 성공: {userID} -> {newPosition}");
			return true;
		}
		catch (Exception e)
		{
			// Console.WriteLine($"사용자 위치 업데이트 실패: {e.Message}");
			return false;
		}
	}
}