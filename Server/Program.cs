using System;
using System.Net;
using System.Threading.Tasks;
using ServerCore;
using System.Collections.Generic;
using Server.DB;

namespace Server
{
	class Program
	{
		// 싱글톤 DB
		public static DBManager DBManager = new DBManager();
		
		// 싱글톤 리스너
		public static Listener _listener = new Listener();
		
		// 여러 GameRoom을 관리하기 위한 Dictionary 추가
		public static Dictionary<string, GameRoom> GameRooms = new Dictionary<string, GameRoom>();
		
		static async Task Main(string[] args)
		{
			// DB 초기화
			await DBManager.Init();
			
			// 다운로드된 데이터를 기반으로, 모든 씬의 초기 몬스터 및 오브젝트 스폰 세팅 진행
			SpawnManager.Instance.DefaultSceneEntitySetting();
			
			// 서버 작업 진행
			// 모든 네트워크 인터페이스에서 연결을 받을 수 있도록 IPAddress.Any 사용
			IPAddress  ipAddr   = IPAddress.Any; // 0.0.0.0 - 모든 IP에서 접속 허용
			IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);
			
			// 서버는 문지기가 필요.....
			// 클라가 서버에 접속을 성공하면, 서버에서 클라를 관리해줄 ClientSession을 만들어주는
			// SessionManager.Instance.Generate();를 콜백 함수로 등록.
			_listener.Init(endPoint, () => { return SessionManager.Instance.ClientSessionGenerate(); });
			Console.WriteLine("Listening...");
			
			// 무한 루프 작업 스타트
			JobTimer.Instance.Push(FlushRoom,0);
			
			// 무한 루프 시작
			while (true)
			{
				JobTimer.Instance.Flush();
			}
		}
		
		static void FlushRoom()
		{
			// 모든 룸에 들어와 있는 작업들 처리....
			foreach (GameRoom room in GameRooms.Values)
			{
				room.Push(() => room.Flush());
			}
			JobTimer.Instance.Push(FlushRoom, 25);	// 25ms 후에 다시 FlushRoom 호출.(40FPS)
		}
	}
}