using ServerCore;
using System;
using System.Net;
using System.Threading;

// 더미 클라이언트는 서버의 스트레스 테스트를 위해 존재함.
// 그리고, 클라이언트에서도 동일하게 코드 사용.
namespace DummyClient
{
	class Program
	{
		static void Main(string[] args)
		{
			// 기본 세팅
			string      host     = Dns.GetHostName();				 // DNS (Domain Name System)
			IPHostEntry ipHost   = Dns.GetHostEntry(host);
			IPAddress   ipAddr   = ipHost.AddressList[0];
			IPEndPoint  endPoint = new IPEndPoint(ipAddr, 7777);
			
			// 테스트용 더미 클라이언트 생성 (개수 대폭 줄임)
			Console.WriteLine("🤖 DummyClient 시작 - 서버에 연결 중...");
			Console.WriteLine($"📍 연결 대상: {endPoint}");
			Console.WriteLine("⚠️  DummyClient는 테스트 목적으로만 사용하세요!");
			
			Connector connector = new Connector();
			connector.Connect(endPoint, () => { return DummyClientSessionManager.Instance.Generate(); }, 1); // 1개로 줄임

			while (true)
			{
				try
				{
					DummyClientSessionManager.Instance.SendForEach();
				}
				catch (Exception e)
				{
					Console.WriteLine(e.ToString());
				}
				
				Thread.Sleep(50); // 40ms 후에 다시 FlushRoom 호출.(20FPS)
			}
		}
	}
}
