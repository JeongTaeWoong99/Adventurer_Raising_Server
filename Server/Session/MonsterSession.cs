using System;
using ServerCore;
using System.Net;

namespace Server
{
	public class MonsterSession : CommonSession
	{
		public override int EntityType { get; } = (int)Define.Layer.Monster;

		public override void OnConnected(EndPoint endPoint)
		{
			// Console.WriteLine("몬스터 새로 생성 : " + SessionId);
		}

		public override void OnDisconnected(EndPoint endPoint)
		{
			Console.WriteLine("몬스터 삭제 : " + SessionId);
		}
		
		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			Console.WriteLine("몬스터 조작 : " + SessionId);
		}
		
		public override void OnSend(int numOfBytes)
		{
			// 사용 X
		}
	}
}