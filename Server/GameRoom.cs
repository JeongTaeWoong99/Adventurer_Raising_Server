using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Server.DB;

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
			Object  = 7, Monster = 8, //Ground  = 9, Block   = 10,
			Player  = 11,
		}
		
		public enum Anime
		{
			Idle, Run, Dash, Attack, Skill, Pose, Hit, Death,
		}
		
		public GameRoom(string sceneName, DBManager dbManager)
		{
			SceneName         = sceneName;
			Program.DBManager = dbManager;
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
			foreach (ClientSession c in _commonSessions.OfType<ClientSession>())
				c.Send(_pendingList); // 다중 세그먼트 Send를 통해, _pendingList에 들어가 있는 모든 세그먼트 작업들을 보냄.
			
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

		#region 기능

		// 엔티티의 위치를 업데이트하고, 다른 클라이언트에게 알립니다.
		private void EntityInfoChange(CommonSession session)
		{
			// 바뀐 정보 반영은 각각의 애니메이션 or 공격 등등에서 무적 or 레벨 or 체력 변화가 일어나고,
			// 해당 메서드에서 일어난다.
			// 여기서는, 바뀐 정보를 브로드케스트하는 용도로 사용한다.
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityInfoChange change = new S_BroadcastEntityInfoChange {
				ID            = session.SessionId,
				entityType    = session.EntityType,
				currentLevel  = session.CurrentLevel,
				currentHp     = session.CurrentHP,
				live          = session.Live,
				invincibility = session.Invincibility
			};
			
			Broadcast(change.Write());
		}

		#endregion

		#region  관리 영역
		// 클라이언트를 방에 입장시키고, 다른 클라이언트에게 알립니다.
		// ★ 새 플레이어   => List와 Enter(Enter는 무시)
		// ★ 기존 플레이어 => Enter
		// 같은 GameRoom 상태를 패킷으로 공유하고 있다....
		public void NewPlayerSetting(ClientSession session)
		{
			// ☆ 새로 들어온 클라이언트(=나)한테는 모든 세션 목록 전송. => 플레이어 / 오브젝트 / 몬스터 모두
			// ☆ 단, 사망한 엔티티는 제외
			S_BroadcastEntityList entityList = new S_BroadcastEntityList();
			foreach (CommonSession s in _commonSessions)
			{
				if(!s.Live)
					continue;
				S_BroadcastEntityList.Entity entity = new S_BroadcastEntityList.Entity {
					isSelf	      = (s == session),
					entityType    = s.EntityType,
					ID            = s.SessionId,
					serialNumber  = s.SerialNumber,
					nickname      = s.NickName,
					currentLevel  = s.CurrentLevel,
					currentHp     = s.CurrentHP,
					live          = s.Live,
					invincibility = s.Invincibility,
					posX          = s.PosX,
					posY          = s.PosY,
					posZ          = s.PosZ,
					rotationY     = s.RotationY,
					animationID   = s.AnimationId
				};
				entityList.entitys.Add(entity);
			}
			session.Send(entityList.Write()); // 새로 들어온 클라에게 바로! 보내주기 위해서,
											  // Broadcast를 통해, _pendingList에 등록하지 않고
											  // 바로 보내줌.(단일 세그먼트 Send 사용)
											  
			// ☆ 먼저 들어와 있던 클라이언트들 모두에게, 새로운 클라이언트 입장을 알린다.
			// ☆ 나중에 해당 부분에 바리에이션을 넣어서, 랜덤 위치 생성....
			S_BroadcastEntityEnter enter = new S_BroadcastEntityEnter
			{
				entityType    = session.EntityType,
				ID			  = session.SessionId,
				serialNumber  = session.SerialNumber,
				nickname      = session.NickName,
				currentLevel  = session.CurrentLevel,
				currentHp     = session.CurrentHP,
				live          = session.Live,
				invincibility = session.Invincibility,
				posX          = session.PosX,
				posY          = session.PosY,
				posZ          = session.PosZ,
				rotationY     = session.RotationY,
				animationID   = session.AnimationId
			};
			Broadcast(enter.Write()); // 모든 클라에게 보내주기 위해서,
											 // Broadcast를 통해, _pendingList에 등록하고,
											 // 순차적으로 보내줌.(다중 세그먼트 Send 사용)
			Console.WriteLine("NewPlayerSetting 전송 + 브로드캐스트");
		}

		// 엔티티를 방에서 제거하고, 다른 클라이언트에게 알리기
		public void EntityLeave(CommonSession session, C_EntityLeave leave)
		{
			// (1) 떠나는 클라이언트 자기 자신에게는 패킷을 직접 보냄.
			// ☆ 이렇게 하면 대기열을 거치지 않고 즉시 전송되어, 방에서 나가기 전에 메시지를 받을 수 있습니다.☆
			if (session is ClientSession clientSession)
			{
				S_BroadcastEntityLeave selfLeavePacket = new S_BroadcastEntityLeave();
				selfLeavePacket.ID         = session.SessionId;
				selfLeavePacket.entityType = session.EntityType;
				clientSession.Send(selfLeavePacket.Write());
			}
			
			// (2) 방에 남아있는 다른 모든 클라이언트들에게는 "떠난다"는 사실을 브로드캐스트합니다.(이 패킷들은 _pendingList에 추가되었다가 다음 Flush() 타이밍에 전송됩니다.)
			S_BroadcastEntityLeave broadcastPacket = new S_BroadcastEntityLeave();
			broadcastPacket.ID         = session.SessionId;
			broadcastPacket.entityType = session.EntityType;
			Broadcast(broadcastPacket.Write());
			
			// (3) 모든 전송 작업이 예약된 후, 실제 세션 목록에서 제거합니다.
			_commonSessions.Remove(session); 
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
			S_BroadcastEntityMove move = new S_BroadcastEntityMove {
				ID				= session.SessionId,
				entityType		= session.EntityType,
				isInstantAction = packet.isInstantAction, // 즉시 행동 패킷이면, 클라이언트가 패킷을 받았을 때, 정확한 위치로 순간 이동하도록 함...
				posX 			= session.PosX,
				posY 			= session.PosY,
				posZ 			= session.PosZ
			};

			Broadcast(move.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void Rotation(CommonSession session, C_EntityRotation packet)
		{
			// 애니메이션 바꿔주고
			session.RotationY = packet.rotationY;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityRotation rotation = new S_BroadcastEntityRotation {
				ID		   = session.SessionId,
				entityType = session.EntityType,
				rotationY  = session.RotationY
			};

			Broadcast(rotation.Write());	
		}
		
		// 플레이어 애니메이션 처리
		public void Animation(CommonSession session, C_EntityAnimation packet)
		{
			// 세션의 애니메이션 상태 바꿔주고
			session.AnimationId = packet.animationID;
			
			// 애니메이션에 따른, 무적상태 변경
			bool isCurrentInvincibility;
			if		(packet.animationID == 0) isCurrentInvincibility = false;  // Idl
			else if (packet.animationID == 2) isCurrentInvincibility = true;   // Dash
			else							  isCurrentInvincibility = false;  // 기타
			
			// 실시간 정보 변경 알리기(새로 들어온 상태가 현재 상태와 다른 경우)
			if (isCurrentInvincibility != session.Invincibility)
			{
				session.Invincibility = isCurrentInvincibility;	// 내 세션 정보 변경
				EntityInfoChange(session);						// 브로드케스트
			}
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityAnimation anime = new S_BroadcastEntityAnimation {
				ID          = session.SessionId,
				entityType  = session.EntityType,
				dirX        = packet.dirX,
				dirY        = packet.dirY,
				dirZ        = packet.dirZ,
				animationID = packet.animationID
			};

			Broadcast(anime.Write());
		}
		
		// 플레이어 애니메이션 처리
		public void AttackAnimation(CommonSession session, C_EntityAttackAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationID;
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityAttackAnimation attackAnimation = new S_BroadcastEntityAttackAnimation {
				ID		         = session.SessionId,
				entityType       = session.EntityType,
				animationID      = packet.animationID,
				attackAnimeNumID = packet.attackAnimeNumID,
				dirX             = packet.dirX,
				dirY             = packet.dirY,
				dirZ             = packet.dirZ
			};
			Broadcast(attackAnimation.Write());	
		}
		
		// 플레이어 애니메이션 처리
		public void AttackCheckToAttackResult(CommonSession session, C_EntityAttackCheck packet)
		{	
			// 지역 메서드 1 : 공격자 타입에 따른 타겟 목록 반환
			List<CommonSession> GetAttackTargets(int attackerEntityType)
			{
				var targets = new List<CommonSession>();
				
				// 플레이어 공격 -> 오브젝트/몬스터 타겟
				if (attackerEntityType == (int)Layer.Player)
					targets.AddRange(_commonSessions.Where(s => s.EntityType is (int)Layer.Object or (int)Layer.Monster));
				// 오브젝트/몬스터 공격 -> 플레이어 타겟
				else if (attackerEntityType is (int)Layer.Object or (int)Layer.Monster)
					targets.AddRange(_commonSessions.Where(s => s.EntityType == (int)Layer.Player));
			
				return targets;
			}
			
			// 지역 메서드 2 : 히트 처리 (체력 감소, 애니메이션, 위치 동기화, 결과 추가)
			void ProcessHit(CommonSession target, int damage, S_BroadcastEntityAttackResult attackResult)
			{
				// 타겟의 서버에서 상태 변경 및 브로드케스트
				target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
				if (target.CurrentHP  > 0)
				{
					target.AnimationId = (int)Anime.Hit;
				}
				else if (target.CurrentHP == 0)
				{
					target.Live        = false;
					target.AnimationId = (int)Anime.Death; 
				}
				EntityInfoChange(target);
				
				// 히트된 타겟의 위치 즉시 동기화 (isInstantAction = true)
				S_BroadcastEntityMove syncMove = new S_BroadcastEntityMove {
					ID				= target.SessionId,
					entityType		= target.EntityType,
					isInstantAction = true,				
					posX 			= target.PosX,
					posY 			= target.PosY,
					posZ 			= target.PosZ
				};
				Broadcast(syncMove.Write());
			
				// 공격 결과에 타겟 추가
				var hitTarget = new S_BroadcastEntityAttackResult.Entity {
					targetID         = target.SessionId,
					targetEntityType = target.EntityType
				};
				attackResult.entitys.Add(hitTarget);
			}
			
			// 지역 메서드 3 : OBB(회전된 박스) vs 구(원) 충돌 판정 (XZ 평면만, Y축 제외)
			// 
			// 동작 원리:
			// 1. 타겟의 위치를 공격 박스의 로컬 좌표계로 변환
			// 2. 로컬 좌표계에서 구의 중심에서 OBB까지의 최단 거리 계산
			// 3. 최단 거리가 타겟의 반지름보다 작으면 충돌
			// 
			// 장점:
			// - OBB vs OBB보다 계산이 단순함
			// - 타겟의 크기를 정확하게 고려
			// - Y축 체크를 제외하여 성능 최적화
			// - Unity/Unreal의 기본 콜라이더와 호환성이 좋음
			bool IsInAttackRange(C_EntityAttackCheck packet, CommonSession target)
			{
				// attackSerial을 통해 공격 정보 가져오기
				AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
				if (attackInfo == null)
				{
					Console.WriteLine($"공격 정보를 찾을 수 없습니다: {packet.attackSerial}");
					return false;
				}
				
				// range 정보 파싱 (예: "1.0 / 1.0 / 1.0")
				string[] rangeParts = attackInfo.range.Split('/');
				if (rangeParts.Length != 3)
				{
					Console.WriteLine($"잘못된 range 형식: {attackInfo.range}");
					return false;
				}
				
				float rangeX = float.Parse(rangeParts[0].Trim());
				float rangeZ = float.Parse(rangeParts[2].Trim()); 
				
				float checkRangeX = rangeX * 2; // range가 반지름이면 2를 곱해 전체 길이로 변환
				float checkRangeZ = rangeZ * 2;
				
				// 공격 박스의 중심점과 크기(Y축 제외)
				float centerX   = packet.poxX;
				float centerZ   = packet.poxZ;
				float rotationY = packet.rotationY;
				
				// 타겟의 현재 위치와 반지름(Y축 제외)
				float targetX      = target.PosX;
				float targetZ      = target.PosZ;
				float targetRadius = target.Body_Size; // characterInfos의 body_Size를 세션에 B_Size로 세팅함
				
				// 1. 타겟의 위치를 공격 박스의 로컬 좌표계로 변환 (XZ 평면만)
				// 1.1 공격 중심점 기준 상대 좌표 계산
				float relativeX = targetX - centerX;
				float relativeZ = targetZ - centerZ;
				
				// 1.2 Y축 회전 행렬 적용 (라디안으로 변환)
				float cosY = (float)Math.Cos(-rotationY * Math.PI / 180.0f);
				float sinY = (float)Math.Sin(-rotationY * Math.PI / 180.0f);
				
				// 1.3 회전된 좌표계로 변환 (XZ 평면만)
				float localX = relativeX * cosY - relativeZ * sinY;
				float localZ = relativeX * sinY + relativeZ * cosY;
				
				// 2. OBB의 각 면까지의 최단 거리 계산 (XZ 평면만)
				float halfX = checkRangeX * 0.5f;
				float halfZ = checkRangeZ * 0.5f;
				
				// 2.1 각 축에서의 최단 거리 계산 (XZ 평면만)
				float dx = Math.Max(0, Math.Abs(localX) - halfX);
				float dz = Math.Max(0, Math.Abs(localZ) - halfZ);
				
				// 2.2 구의 중심에서 OBB까지의 최단 거리 계산 (XZ 평면만)
				float distanceSquared = dx * dx + dz * dz;
				
				// 3. 최단 거리가 타겟의 반지름보다 작으면 충돌
				return distanceSquared <= (targetRadius * targetRadius);
			}		
			
			// attackSerial을 통해 공격 정보 가져오기
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
			if (attackInfo == null)
			{
				//Console.WriteLine($"공격 정보를 찾을 수 없습니다: {packet.attackSerial}");
				return;
			}
		
			S_BroadcastEntityAttackResult attackResult = new S_BroadcastEntityAttackResult {
				attackerID         = session.SessionId,
				attackerEntityType = session.EntityType,
				effectSerial       = attackInfo.effectSerial
			};
			
			// 데미지 계산 (기본 공격력 * 데미지 배수)
			float baseDamage       = session.Damage;
			float damageMultiplier = float.Parse(attackInfo.damageMultiplier);
			int finalDamage        = (int)(baseDamage * damageMultiplier);
			attackResult.damage    = finalDamage;
		
			// 공격자 타입에 따른 타겟 필터링 및 처리
			var targets = GetAttackTargets(session.EntityType);
			foreach (var target in targets)
			{
				if (!target.Live || target.Invincibility)
				{
					//Console.WriteLine(target.NickName + "이 사망 or 무적이라 체크 제외");
					continue;
				}
				
				if (IsInAttackRange(packet, target))
				{
					ProcessHit(target, finalDamage, attackResult);
				}
				else
				{
					string attackerType = session.EntityType == (int)Layer.Player ? "플레이어" : "오몬";
					//Console.WriteLine($"{attackerType} 공격이 {target.SessionId}에 X!");
				}
			}
			
			// 공격 결과 브로드캐스트
			if (attackResult.entitys.Count > 0)
			{
				Broadcast(attackResult.Write());
			}
		}
		
		#endregion
	}
}