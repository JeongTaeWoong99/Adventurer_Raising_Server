using System;
using System.Net;
using System.Threading.Tasks;
using ServerCore;
using System.Collections.Generic;

namespace Server
{
	class Program
	{
		// 싱글톤 리스너
		static Listener _listener = new Listener();
		
		// 여러 GameRoom을 관리하기 위한 Dictionary 추가
		public static Dictionary<string, GameRoom> GameRooms = new Dictionary<string, GameRoom>();
		
		static void FlushRoom()
		{
			// 모든 룸에 들어와 있는 작업들 처리....
			foreach (GameRoom room in GameRooms.Values)
			{
				room.Push(() => room.Flush());
			}
			JobTimer.Instance.Push(FlushRoom, 25);	// 25ms 후에 다시 FlushRoom 호출.(40FPS)
		}
		
		static async Task Main(string[] args)
		{
			// Firestore에서 데이터 다운로드 및 JSON 저장
			var downloader = new FirebaseDataDownloader
			(
				"d-rpg-server",																											// 프로젝트 ID																										
				@"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Firebase\d-rpg-server-firebase-adminsdk-fbsvc-cc3363d61c.json"  // 서비스 계정 키 파일 경로
			);
			await downloader.DownloadAllAsync();
			
			// 다운로드된 데이터를 기반으로, 모든 씬의 몬스터 및 오브젝트 스폰
			SpawnManager.Instance.SpawnAllEntities();
		
			// 서버 작업 진행
			string      host     = Dns.GetHostName();   		  // DNS (Domain Name System)
			IPHostEntry ipHost   = Dns.GetHostEntry(host);
			IPAddress   ipAddr   = ipHost.AddressList[0];
			IPEndPoint  endPoint = new IPEndPoint(ipAddr, 7777);
			
			// 서버는 문지기가 필요.....
			// 클라가 서버에 접속을 성공하면, 서버에서 클라를 관리해줄 ClientSession을 만들어주는
			// SessionManager.Instance.Generate();를 콜백 함수로 등록.
			_listener.Init(endPoint, () => { return SessionManager.Instance.ClientSessionGenerate(); });
			Console.WriteLine("Listening...");
			
			// 무한 루프 작업 스타트
			JobTimer.Instance.Push(FlushRoom,0);
			
			// 무한 루프
			while (true)
			{
				JobTimer.Instance.Flush();
			}
			
			// FlushRoom 작업 등록 (250ms 간격) ←
			// 	↓							  ↑
			// while(true) 루프 시작			  ↑
			// 	↓							  ↑
			// JobTimer.Flush() 호출			  ↑
			// 	↓							  ↑
			//  250ms가 지났는지 체크함.		  ↑
			// 	지났다면    -> FlushRoom 실행   ↑
			// 	안 지났다면 -> 다음 작업 체크	  ↑
			// 	↓							  ↑	
			//  →→→→→→→→→→→→→→→→→→→→→→→→→→→→→→↑
		}
	}
}
