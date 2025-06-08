using System;
using System.Net;

namespace Server
{
	public class ObjectSession : CommonSession
	{
		public override int EntityType { get; } = (int)GameRoom.Layer.Object;
		
		public override void OnConnected(EndPoint endPoint)
		{
			Console.WriteLine("오브젝트 새로 생성 : " + SessionId);
		}

		public override void OnDisconnected(EndPoint endPoint)
		{
			Console.WriteLine("오브젝트 삭제 : " + SessionId);
		}
		
		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			Console.WriteLine("오브젝트 조작 : " + SessionId);
		}
		
		public override void OnSend(int numOfBytes)
		{
			// 사용 X
		}
	}
}