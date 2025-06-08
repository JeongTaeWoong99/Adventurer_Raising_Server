using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Server
{
	// <상속 구조>
	// Server의 GameRoom / ServerCore의 JobQueue <- IJobQueue
	// GameRoom 클래스는 게임 내의 방을 관리하며, 클라이언트 세션을 관리하고 메시지를 브로드캐스트함.
	public class GameRoom : IJobQueue
	{
		# region 기본 구성요소
		// GameRoom에 씬 이름을 넣어서, 룸을 나누기
		public string SceneName { get; private set; }
		
		// ServerCore의 JobQueue를 사용하기 위해, 생성.
		// ★ 단 하나를 만들어서, 단일 쓰레드를 이용해 사용한다!
		JobQueue _jobQueue = new JobQueue();
		
		// 세션 목록(방에 들어와 있는 오브젝트들(플레이어/오브젝트/몬스터)의 대리자)
		public List<CommonSession> _commonSessions = new List<CommonSession>();
		
		// 전송 대기 중인 메시지 목록(JobQueue에 액션을 Push 및 Flush를 통해, 순차적으로 저장됨.)
		private List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
		
		public enum Layer
		{
			Object  = 7, Monster = 8,
			//Ground  = 9, Block   = 10,
			Player  = 11,
		}
		
		public GameRoom(string sceneName)
		{
			SceneName = sceneName;
			//Console.WriteLine($"GameRoom '{SceneName}' 초기화 완료.");
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
			foreach (ClientSession s in _commonSessions.OfType<ClientSession>())
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
		
		# endregion

		#region  관리 영역
		// 클라이언트를 방에 입장시키고, 다른 클라이언트에게 알립니다.
		// ★ 처음 입장   => Enter와 List
		// ★ 나중에 입장 => List
		// 같은 GameRoom 상태를 패킷으로 공유하고 있다....
		public void NewPlayerEnter(ClientSession session)
		{
			// ☆ 새로 들어온 클라이언트한테는 모든 세션 목록 전송. => 플레이어 / 오브젝트 / 몬스터 모두 다
			S_BroadcastEntityList entityList = new S_BroadcastEntityList();
			foreach (CommonSession s in _commonSessions)
			{
				entityList.entitys.Add(new S_BroadcastEntityList.Entity()
				{
					isSelf      = (s == session),	// 새로 들어오는 세션과 목록의 세션이 같으면, 자신이다...
					
					entityType   = s.EntityType,
					
					serialNumber = s.serialNumber,
					nickname     = s.nickname,
					currentLevel = s.currentLevel,
					currentHp    = s.currentHP,
						
					ID			  = s.SessionId,	   
					live          = s.Live,
					invincibility = s.Invincibility,
					posX          = s.PosX,
					posY          = s.PosY,
					posZ          = s.PosZ,
					rotationY     = s.RotationY,
					animationID   = s.AnimationId,
				});
			}
			session.Send(entityList.Write()); // 새로 들어온 클라에게 바로! 보내주기 위해서,
											  // Broadcast를 통해, _pendingList에 등록하지 않고
											  // 바로 보내줌.(단일 세그먼트 Send 사용)
			
			// ★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆
			// ☆ 먼저 들어와 있던 클라이언트들 모두에게, 새로운 클라이언트 입장을 알린다.
			// ☆ 나중에 해당 부분에 바리에이션을 넣어서, 랜덤 위치 생성....
			S_BroadcastEntityEnter enter = new S_BroadcastEntityEnter();
			enter.entityType   = session.EntityType;
			
			enter.serialNumber = session.serialNumber;
			enter.nickname     = session.nickname;
			enter.currentLevel = session.currentLevel;
			enter.currentHp    = session.currentHP;
			
			enter.ID	      = session.SessionId;
			enter.posX        = session.PosX;	// DB에 저장된 정보를 앞서 받아와서 세팅!
			enter.posY        = session.PosY;	// DB에 저장된 정보를 앞서 받아와서 세팅!
			enter.posZ        = session.PosZ;	// DB에 저장된 정보를 앞서 받아와서 세팅!
			enter.rotationY   = 180f;
			enter.animationID = 0;
			Broadcast(enter.Write()); // 모든 클라에게 보내주기 위해서,
											 // Broadcast를 통해, _pendingList에 등록하고,
											 // 순차적으로 보내줌.(다중 세그먼트 Send 사용)
		}

		// 엔티티를 방에서 제거하고, 다른 클라이언트에게 알리기
		public void EntityLeave(CommonSession session)
		{
			// 엔티티 방에서 제거
			_commonSessions.Remove(session);
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityLeaveGame leave = new S_BroadcastEntityLeaveGame();
			leave.ID         = session.SessionId;
			leave.entityType = session.EntityType;
			Broadcast(leave.Write());
		}
		
		// 엔티티의 위치를 업데이트하고, 다른 클라이언트에게 알립니다.
		public void EntityInfoChange(CommonSession session, C_EntityInfoChange packet)
		{
			// 바뀐 정보 반영해주고
			// Console.WriteLine(session.SessionId +"의 무적상태 " + session.Invincibility + " => " +  packet.invincibility);
			session.currentLevel  = packet.currentLevel;
			session.currentHP     = packet.currentHp;
			session.Invincibility = packet.invincibility;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityInfoChange change = new S_BroadcastEntityInfoChange();
			change.ID	        = session.SessionId;
			change.entityType   = session.EntityType;	
			
			change.currentLevel  = session.currentLevel;
			change.currentHp     = session.currentHP;
			change.invincibility = session.Invincibility;
			Broadcast(change.Write());
		}
		
		#endregion
	
		#region 조작 영역
		// 클라이언트의 위치를 업데이트하고, 다른 클라이언트에게 알립니다.
		public void Move(CommonSession session, C_EntityMove packet)
		{
			// 좌표 바꿔주고
			session.PosX = packet.posX;
			session.PosY = packet.posY;
			session.PosZ = packet.posZ;
		
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityMove move = new S_BroadcastEntityMove();
			move.ID         = session.SessionId;
			move.entityType = session.EntityType;
			
			move.posX       = session.PosX;
			move.posY       = session.PosY;
			move.posZ       = session.PosZ;
			Broadcast(move.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void Rotation(CommonSession session, C_EntityRotation packet)
		{
			// 애니메이션 바꿔주고
			session.RotationY = packet.rotationY;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityRotation rotation = new S_BroadcastEntityRotation();
			rotation.ID                  = session.SessionId;	 
			rotation.entityType          = session.EntityType;
			
			rotation.rotationY           = session.RotationY; 
			Broadcast(rotation.Write());	
		}
		
		// 플레이어 애니메이션 처리
		public void Animation(CommonSession session, C_EntityAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationID;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityAnimation anime = new S_BroadcastEntityAnimation();
			anime.ID            = session.SessionId;  
			anime.entityType    = session.EntityType;
			
			anime.animationID   = packet.animationID;
			Broadcast(anime.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void AttackAnimation(CommonSession session, C_EntityAttackAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationID;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityAttackAnimation attackAnimation = new S_BroadcastEntityAttackAnimation();
			attackAnimation.ID               = session.SessionId;
			attackAnimation.entityType       = session.EntityType;
			
			attackAnimation.animationID      = packet.animationID;		  
			attackAnimation.attackAnimeNumID = packet.attackAnimeNumID;
			attackAnimation.posX             = packet.posX;
			attackAnimation.posY             = packet.posY;
			attackAnimation.posZ             = packet.posZ;
			Broadcast(attackAnimation.Write());	
		}
		
		// 플레이어 애니메이션 처리
		public void AttackCheckToAttackResult(CommonSession session, C_EntityAttackCheck packet)
		{
			Console.WriteLine("AttackCheckToAttackResult까지 들어옴... AttackCheck를 가지고, AttackResult 리스트를 만들기...");
			
			// // 애니메이션 바꿔주고
			// session.AnimationId = packet.animationID;
			//
			// // 모두에게 알리기 위해, 대기 목록에 추가
			// S_BroadcastEntityAttackAnimation attackAnimation = new S_BroadcastEntityAttackAnimation();
			// attackAnimation.ID               = session.SessionId;
			// attackAnimation.entityType       = session.EntityType;
			//
			// attackAnimation.animationID      = packet.animationID;		  
			// attackAnimation.attackAnimeNumID = packet.attackAnimeNumID;
			// attackAnimation.posX             = packet.posX;
			// attackAnimation.posY             = packet.posY;
			// attackAnimation.posZ             = packet.posZ;
			// Broadcast(attackAnimation.Write());	
		}
		
		#endregion
	}
}