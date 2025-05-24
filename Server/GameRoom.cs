using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Server
{
	// <상속 구조>
	// Server의 GameRoom / ServerCore의 JobQueue <- IJobQueue
	// GameRoom 클래스는 게임 내의 방을 관리하며, 클라이언트 세션을 관리하고 메시지를 브로드캐스트함.
	class GameRoom : IJobQueue
	{
		// ServerCore의 JobQueue를 사용하기 위해, 생성.
		// ★ 단 하나를 만들어서, 단일 쓰레드를 이용해 사용한다!
		JobQueue _jobQueue = new JobQueue();
		
		// 클라이언트 세션 목록(방에 들어와 있는 클라이언트의 대리자)
		List<ClientSession> _sessions = new List<ClientSession>();
			
		// 전송 대기 중인 메시지 목록(JobQueue에 액션을 Push 및 Flush를 통해, 순차적으로 저장됨.)
		List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
		
		public enum Layer
		{
			Object  = 7,
			Monster = 8,
			//Ground  = 9, Block   = 10,
			Player  = 11,
		}
		
		// ServerCore의 JobQueue에 작업 푸쉬한다.
		// GameRoom의 Enter(),Leave(),Move(),Flush() 작업들이 들어간다.
		// Action은 단일 쓰레드로 처리되어, 순차적으로 _pendingList에 등록된다.
		public void Push(Action job)
		{
			_jobQueue.Push(job);
		}
		
		// 대기 중인 작업들을 모든 클라이언트에게 전송하고 목록을 비우기.
		// ★ 서버의 Program.cs의 Main에서 정해진 초마다, 무한으로 루프하면서, 작동.
		// 쌓여있던 _pendingList를 비워주는 역할.(Flush()전에 쌓여있던 작업들만)
		// 락을 잡지 않는 이유 JobQueue를 사용하기 때문에
		public void Flush()
		{
			// ☆ ServerSession/ClientSession <- PacketSession <- Session
			// ☆ 클라이언트 각자의 ClientSession, 즉 ServerCore의 Session의 Send에서 처리.
			foreach (ClientSession s in _sessions)
				s.Send(_pendingList); // 다중 세그먼트 Send를 통해, _pendingList에 들어가 있는 모든 세그먼트 작업들을 보냄.
			
			_pendingList.Clear();
		}
		
		// 대기 목록에 추가(패킷을 바로 보내지 않고 일단 저장을 해놓는다.)
		// ServerCore의 JobQueue 단일 쓰레드에서 처리된 작업들 _pendingList에 넣어주기.
		// ★ Broadcast를 어디까지 해줄지는 심화의 영역.....시야 or 범위 등등
		public void Broadcast(ArraySegment<byte> segment)
		{
			_pendingList.Add(segment);			
		}

		// 클라이언트를 방에 입장시키고, 다른 클라이언트에게 알립니다.
		public void Enter(ClientSession session)
		{
			// 새로운 플레이어를 관리할 ClientSession추가
			_sessions.Add(session);
			session.Room = this;
			
			// ☆ 새로 들어온 클라이언트한테는 모든 플레이어 목록 전송.
			S_BroadcastPlayerList players = new S_BroadcastPlayerList();
			foreach (ClientSession s in _sessions)
			{
				players.players.Add(new S_BroadcastPlayerList.Player()
				{
					isSelf = (s == session), // 자기자신 bool isSelf로 판단.
					playerId = s.SessionId,
					posX = s.PosX,
					posY = s.PosY,
					posZ = s.PosZ,
				});
			}
			session.Send(players.Write()); // 새로 들어온 클라에게 바로! 보내주기 위해서,
										   // Broadcast를 통해, _pendingList에 등록하지 않고
										   // 바로 보내줌.(단일 세그먼트 Send 사용)
										   
			// ☆ 먼저 들어와 있던 클라이언트들 모두에게, 새로운 클라이언트 입장을 알린다.
			S_BroadcastPlayerEnterGame enter = new S_BroadcastPlayerEnterGame();
			enter.playerId = session.SessionId;
			enter.posX = 0;
			enter.posY = 0;
			enter.posZ = 0;
			Broadcast(enter.Write()); // 모든 클라에게 보내주기 위해서,
											 // Broadcast를 통해, _pendingList에 등록하고,
											 // 순차적으로 보내줌.(다중 세그먼트 Send 사용)
		}

		// 클라이언트를 방에서 제거하고, 다른 클라이언트에게 알리기
		public void Leave(ClientSession session)
		{
			// 플레이어 제거하고
			_sessions.Remove(session);

			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastPlayerLeaveGame leave = new S_BroadcastPlayerLeaveGame();
			leave.playerId = session.SessionId;
			Broadcast(leave.Write());
		}

		// 클라이언트의 위치를 업데이트하고, 다른 클라이언트에게 알립니다.
		public void Move(ClientSession session, C_Move packet)
		{
			// 좌표 바꿔주고
			session.PosX = packet.posX;
			session.PosY = packet.posY;
			session.PosZ = packet.posZ;

			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastMove move = new S_BroadcastMove();
			move.Id   = session.SessionId;
			move.posX = session.PosX;
			move.posY = session.PosY;
			move.posZ = session.PosZ;
			Broadcast(move.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void Animation(ClientSession session, C_Animation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationId;
			Console.WriteLine(session.SessionId + "의 애니메이션 아이디는 " +session.AnimationId);
			
			// 플레이어들에게 발신할 애니메이션 객체 생성
			S_BroadcastAnimation anime = new S_BroadcastAnimation();
			anime.Id          = session.SessionId;	// 구분 ID
			anime.animationId = packet.animationId; // 애니메이션 ID
			
			// 만든 애니메이션 패킷을 해당 GameRoom 내 플레이어들에게 전송
			Broadcast(anime.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void AttackAnimation(ClientSession session, C_AttackAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationId;
			Console.WriteLine(session.SessionId + "의 애니메이션 아이디는 " + session.AnimationId);
			
			// 플레이어들에게 발신할 공격 애니메이션 객체 생성
			S_BroadcastAttackAnimation attackAnimation = new S_BroadcastAttackAnimation();
			attackAnimation.Id               = session.SessionId;	   // 구분 ID
			attackAnimation.attackerType     = (int)Layer.Player;
			attackAnimation.animationId      = packet.animationId;		  
			attackAnimation.attackAnimeNumId = packet.attackAnimeNumId; // 노멀 공격 애니메이션 번호
			attackAnimation.posX             = packet.posX;
			attackAnimation.posY             = packet.posY;
			attackAnimation.posZ             = packet.posZ;
			
			// 만든 애니메이션 패킷을 해당 GameRoom 내 플레이어들에게 전송
			Broadcast(attackAnimation.Write());
		}
	}
}