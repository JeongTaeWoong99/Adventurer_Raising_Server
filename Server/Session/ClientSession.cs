using System;
using ServerCore;
using System.Net;

namespace Server
{
	// ★ 서버에 클라이언트가 접속을 할 때마다 ClientSession(대리인) 계속 생성이 된다.
	
	// <서버에서 클라이언트 일을 하는 대리자>
	// 	⇒ Server.ClientSession에서 Packet을 수신  ⇒  Call OnRecvPacket                        
	// 	⇒ Call ClientSessionn.RecvPacket         ⇒  Call PacketManager.Instance.OnRecvPacket
	//  ⇒ Call PacketManager.MakePacket          ⇒  Call PacketHandler에서 해당하는 처리
	public class ClientSession : CommonSession
	{
		public override int EntityType { get; } = (int)GameRoom.Layer.Player;
		
		public override void OnConnected(EndPoint endPoint)
		{
			Console.WriteLine("연결됨 : " + SessionId);
			// 서버에 클라이언트가 접속을 했다면 강제로 방에 들어오게 만듬.
			// 하지만 실제 게임에서는 클라이언트 쪽에서 모든 리소스 업데이트가 완료 되었을 때,
			// 서버에 신호를 보내고 그때 방에 들어오는 작업을 해줘야 한다.
			// 연결되면, 일단 연결된 클라이언트에게 정보를 요청한다.
			// 아직 방에 들어가 있지 않기 때문에, Program.Room.Push를 통해, 작업을 수행하는게 아닌, 배정 전, 즉 방 밖에서 작업을 수행한다...
			// ☆ 새로 들어온 클라이언트한테는 정보 요청 보내기 (바로 보내줌. 단일 세그먼트 Send 사용)
			S_RequestNewPlayerState requestState = new S_RequestNewPlayerState(); // 패킷 생성
			requestState.ID = SessionId;									// 자동 생성된 패킷 ID 사용
			Send(requestState.Write());										// Session의 Send를 통해, 바로 보내주기
		}
		
		// 서버에 있는 클라이언트 세션에서 클라이언트가 보낸 패킷을 받았을 때,
		// byte array로 보낸 패킷을 읽어서 패킷 아이디에 따라 패킷의 정보를 읽고(== 역직렬화)
		// 새롭게 생성한 해당 패킷의 객체에 할당하는 과정이다.
		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			// PacketManager == ServerPacketManager.cs
			// ★ 콜백 == null으로 넘겨주고, PacketHandler처리함.(서버 or 더미클라에서는 따로 처리할 필요 없음.)
			PacketManager.Instance.OnRecvPacket(this, buffer);
		}
		
		// 클라쪽에서 명시적으로 나가지 않고,강제종료된 경우는
		// ClientSession.cs의 OnDisconnected에서 감지 후, 방에서 나가게 한다.
		public override void OnDisconnected(EndPoint endPoint)
		{
			SessionManager.Instance.Remove(this);
			if (Room != null)
			{
				GameRoom room = Room;
				room.Push(() => room.EntityLeave(this));
				Room = null;
			}

			Console.WriteLine("연결 끊김 : " + SessionId);
		}

		public override void OnSend(int numOfBytes)
		{
			//Console.WriteLine($"Transferred bytes: {numOfBytes}");
		}
	}
}
