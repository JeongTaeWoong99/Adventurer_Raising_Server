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
		public override int EntityType { get; } = (int)Define.Layer.Player;
		
		public override void OnConnected(EndPoint endPoint)
		{
			Console.WriteLine("연결됨 : " + SessionId + ". 세션이 만들어지고, 방에는 들어가지 않은 상태.");
		}
		
		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			// 서버에 있는 클라이언트 세션에서 클라이언트가 보낸 패킷을 받았을 때,
			// byte array로 보낸 패킷을 읽어서 패킷 아이디에 따라 패킷의 정보를 읽고(== 역직렬화)
			// 새롭게 생성한 해당 패킷의 객체에 할당하는 과정이다.
			// PacketManager == ServerPacketManager.cs
			// ★ 콜백 == null으로 넘겨주고, PacketHandler처리함.(서버 or 더미클라에서는 따로 처리할 필요 없음.)
			PacketManager.Instance.OnRecvPacket(this, buffer);
		}
		
		public override void OnDisconnected(EndPoint endPoint)
		{
			// 클라쪽에서 명시적으로 나가지 않고,강제종료된 경우는
			// ClientSession.cs의 OnDisconnected에서 감지 후, 방에서 나가게 한다.
			Console.WriteLine("연결 끊김 : " + SessionId);
			
			// 사망으로 인한 연결 끊김 => Village로 씬 변경 / 마을 중앙 위치 변경 / HP 변경
			if (CurrentHP <= 0 || !Live)
			{
				_ = Program.DBManager._realTime.UpdateUserSceneAsync   (email,   "Village");	// 씬
				_ = Program.DBManager._realTime.UpdateUserPositionAsync(email, "0 / 0 / 0");	// 위치
				_ = Program.DBManager._realTime.UpdateUserHpAsync      (email, MaxHP.ToString());				// HP(최대 체력 회복 => 마을로 돌아가면서, 세션 HP는 복구됨.)
				_ = Program.DBManager._realTime.UpdateLevelAsync       (email,CurrentLevel.ToString());
				_ = Program.DBManager._realTime.UpdateUserExpAsync     (email,currentExp.ToString());

			}
			// 비정상 or 명시적 종료로 연결 끊김 => 현재 위치 / HP 저장
			else
			{
				_ = Program.DBManager._realTime.UpdateUserPositionAsync(email, PosX + " / " + PosY + " / " + PosZ);	// 위치
				_ = Program.DBManager._realTime.UpdateUserHpAsync      (email, CurrentHP.ToString());									// HP(현재 체력 저장)
				_ = Program.DBManager._realTime.UpdateLevelAsync       (email,CurrentLevel.ToString());
				_ = Program.DBManager._realTime.UpdateUserExpAsync     (email,currentExp.ToString());
			}
			
			// 세션 제거 및 룸에서 제거
			SessionManager.Instance.Remove(this);
			if (Room != null)
			{
				GameRoom room = Room;
				C_EntityLeave dummyPtk = new C_EntityLeave();
				room.Push(() => room.EntityLeave(this, dummyPtk));
				Room = null;
			}
		}

		public override void OnSend(int numOfBytes)
		{
			//Console.WriteLine($"Transferred bytes: {numOfBytes}");
		}
	}
}
