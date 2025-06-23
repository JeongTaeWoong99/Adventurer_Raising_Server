using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Server.DB;
using System.Threading.Tasks;

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
		
		// ★ 단 하나를 만들어서, 단일 쓰레드를 이용해 사용한다! ServerCore의 JobQueue를 사용하기 위해, 생성.
		JobQueue _jobQueue = new JobQueue();
		
		// 세션 목록(방에 들어와 있는 오브젝트들(플레이어/오브젝트/몬스터)의 대리자)
		public List<CommonSession> _commonSessions = new List<CommonSession>();
		
		// 전송 대기 중인 메시지 목록(JobQueue에 액션을 Push 및 Flush를 통해, 순차적으로 저장됨.)
		private List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
		
		public GameRoom(string sceneName, DBManager dbManager)
		{
			SceneName         = sceneName;
			Program.DBManager = dbManager;
			//Console.WriteLine($"GameRoom '{SceneName}' 초기화 완료.");
		}
		
		public void Push(Action job)
		{
			// ServerCore의 JobQueue에 작업 푸쉬한다.
			// GameRoom의 Enter(),Leave(),Move(),Flush() 작업들이 들어간다.
			// Action은 단일 쓰레드로 처리되어, 순차적으로 _pendingList에 등록된다.
			_jobQueue.Push(job);
		}
		
		public void Flush()
		{
			// 대기 중인 작업들을 모든 클라이언트에게 전송하고 목록을 비우기.
			// ★ 서버의 Program.cs의 Main에서 정해진 초마다, 무한으로 루프하면서, 작동.
			// 쌓여있던 _pendingList를 비워주는 역할.(Flush()전에 쌓여있던 작업들만)
			// 락을 잡지 않는 이유 JobQueue를 사용하기 때문에
			// ☆ ServerSession/ClientSession <- PacketSession <- Session
			// ☆ 클라이언트 각자의 ClientSession, 즉 ServerCore의 Session의 Send에서 처리.
			// 컬렉션 수정 중 순회 문제 해결 - 안전한 복사본 생성
			var clientSessions = _commonSessions.OfType<ClientSession>().ToList();
			foreach (ClientSession c in clientSessions)
				c.Send(_pendingList); // 다중 세그먼트 Send를 통해, _pendingList에 들어가 있는 모든 세그먼트 작업들을 보냄.
			
			_pendingList.Clear();
		}

		public void Broadcast(ArraySegment<byte> segment)
		{
			// 대기 목록에 추가(패킷을 바로 보내지 않고 일단 저장을 해놓는다.)
			// ServerCore의 JobQueue 단일 쓰레드에서 처리된 작업들 _pendingList에 넣어주기.
			// ★ Broadcast를 어디까지 해줄지는 심화의 영역.....시야 or 범위 등등
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
		
		/// <summary>
		/// 히트 시 넉백 이동 처리
		/// - attacker : 공격자 세션 (위치 기준)
		/// - target   : 피격 대상 (몬스터/오브젝트)
		/// </summary>
		private void StartHitKnockBack(CommonSession target, float hitFromX, float hitFromZ)
		{
			// 이동 속도나 길이가 없으면 패스
			if (target.moveSpeed <= 0 || target.hitLength <= 0)
				return;

			// 방향 벡터 계산 (hitCenter → target) 후 반대 방향으로 이동
			float dirX = target.PosX - hitFromX;
			float dirZ = target.PosZ - hitFromZ;
			float mag  = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
			if (mag <= 0.0001f)
				return;
			dirX /= mag;
			dirZ /= mag;
			
			float moveX = dirX;
			float moveZ = dirZ;
			target.PosX += moveX / 5f; // 노멀라이즈된 값에서 약간만 이동 하도록
			target.PosZ += moveZ / 5f; // 노멀라이즈된 값에서 약간만 이동 하도록
			
			//Console.WriteLine($"[KnockbackDir] targetSession={target.SessionId} dir=({dirX:F3},{dirZ:F3}) hitCenter=({hitFromX:F2},{hitFromZ:F2}) targetPos=({target.PosX:F2},{target.PosZ:F2})");
			S_BroadcastEntityMove mv = new S_BroadcastEntityMove {
				ID              = target.SessionId,
				entityType      = target.EntityType,
				isInstantAction = true,			
				posX            = target.PosX,
				posY            = target.PosY,
				posZ            = target.PosZ
			};
			Broadcast(mv.Write());
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
		
		// 엔티티 로테이션
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
		
		// 엔티티 애니메이션 처리
		public void Animation(CommonSession session, C_EntityAnimation packet)
		{
			// 세션의 애니메이션 상태 바꿔주고
			session.AnimationId = packet.animationID;
			
			// NEW: ScheduleManager에 애니메이션 상태 등록 (자동 전환 관리)
			var animationType = (Define.Anime)packet.animationID;
			ScheduleManager.Instance.SetAnimationState(session, animationType);
			
			// 애니메이션에 따른, 무적상태 변경
			// 실시간 정보 변경 알리기(새로 들어온 상태가 현재 상태와 다른 경우)
			if (false != session.Invincibility)
			{
				session.Invincibility = false;	// 내 세션 정보 변경
				EntityInfoChange(session);						// 브로드케스트
			}
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityAnimation anime = new S_BroadcastEntityAnimation {
				ID          = session.SessionId,
				entityType  = session.EntityType,
				animationID = packet.animationID
			};

			Broadcast(anime.Write());
		}
		
		// 플레이어 대쉬 처리
		public void Dash(CommonSession session, C_EntityDash packet)
		{
			// 세션의 애니메이션 상태 바꿔주고
			session.AnimationId = packet.animationID;
			
			// NEW: ScheduleManager에 애니메이션 상태 등록 (자동 전환 관리)
			var animationType = (Define.Anime)packet.animationID;
			ScheduleManager.Instance.SetAnimationState(session, animationType);
			
			// 애니메이션에 따른, 무적상태 변경
			// 실시간 정보 변경 알리기(새로 들어온 상태가 현재 상태와 다른 경우)
			if (true != session.Invincibility)
			{ 
				session.Invincibility = true;  // 내 세션 정보 변경
				EntityInfoChange(session);	   // 브로드케스트
			}
			
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastEntityDash anime = new S_BroadcastEntityDash {
				ID          = session.SessionId,
				entityType  = session.EntityType,
				animationID = packet.animationID,
				dirX        = packet.dirX,
				dirY        = packet.dirY,
				dirZ        = packet.dirZ,
			};
			Broadcast(anime.Write());
		}
		
		// NEW: 플레이어 애니메이션 처리 (ScheduleManager 연동)
		public void AttackAnimation(CommonSession session, C_EntityAttackAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationID;
			
			// NEW: ScheduleManager에 공격 애니메이션 상태 등록 (공격 번호 포함, 자동 타이밍 관리)
			ScheduleManager.Instance.SetAnimationState(session, Define.Anime.Attack, packet.attackAnimeNumID);
			
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
				if (attackerEntityType == (int)Define.Layer.Player)
					targets.AddRange(_commonSessions.Where(s => s.EntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster));
				// 오브젝트/몬스터 공격 -> 플레이어 타겟
				else if (attackerEntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster)
					targets.AddRange(_commonSessions.Where(s => s.EntityType == (int)Define.Layer.Player));
			
				return targets;
			}
			
			// 지역 메서드 2 : OBB(회전된 박스) vs 구(원) 충돌 판정 (XZ 평면만, Y축 제외)
			bool IsInAttackRange(C_EntityAttackCheck packet, CommonSession target)
			{
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
				float centerX   = packet.attackCenterX;
				float centerZ   = packet.attackCenterZ;
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
				// Console.WriteLine(distanceSquared + " <= (" + targetRadius + " * " + targetRadius + ")");
				return distanceSquared <= (targetRadius * targetRadius);
			}			
			
			// 지역 메서드 3 : 히트 처리 (체력 감소, 애니메이션, 위치 동기화, 결과 추가)
			void ProcessHit(CommonSession target, int damage, S_BroadcastEntityAttackResult attackResult)
			{
				// 타겟의 서버에서 상태 변경 및 브로드케스트
				target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
				if (target.CurrentHP  > 0)
				{
					target.AnimationId = (int)Define.Anime.Hit;
					// ScheduleManager에 Hit 애니메이션 상태 등록 (파이어베이스 hitLength 기반 자동 전환)
					ScheduleManager.Instance.SetAnimationState(target, Define.Anime.Hit);
					// 런 중에 히트를 당했으면, 이동한 시간 만큼 이동
					if (target.AnimationId == (int)Define.Anime.Run)
						ScheduleManager.Instance.ProcessHitDuringRun(target.SessionId);
					// 히트 넉백 이동 시작 (몬스터/오브젝트) : 공격 중심 좌표(packet.poxX/Z)를 사용해 넉백 위치 결정
					StartHitKnockBack(target, packet.attackCenterX, packet.attackCenterZ);
				}
				else if (target.CurrentHP == 0)
				{
					target.Live        = false;
					target.AnimationId = (int)Define.Anime.Death;
					// ScheduleManager에서 애니메이션 상태 제거 (사망으로 더 이상 관리 불필요)
					ScheduleManager.Instance.RemoveAnimationState(target.SessionId);
					// 사망 후 재생성 스케줄링 (10초 후 SpawnManager에 재생성 요청)
					ScheduleDeathAndRespawn(target);
				}
				EntityInfoChange(target);
				
				// 서버 넉백과 동일한 방향 계산 (target -> hitCenter 의 반대)
				float rawDirX = target.PosX - packet.attackCenterX;
				float rawDirZ = target.PosZ - packet.attackCenterZ;
				float mag = (float)Math.Sqrt(rawDirX * rawDirX + rawDirZ * rawDirZ);
				float dirX = (mag > 0.0001f) ? rawDirX / mag : 0f;
				float dirZ = (mag > 0.0001f) ? rawDirZ / mag : 0f;

				var hitTarget = new S_BroadcastEntityAttackResult.Entity {
					targetID         = target.SessionId,
					targetEntityType = target.EntityType,
					hitMoveDirX      = dirX,
					hitMoveDirY      = 0,
					hitMoveDirZ      = dirZ,
				};
				attackResult.entitys.Add(hitTarget);
				//Console.WriteLine($"[ProcessHit] targetId={target.SessionId} dir=({dirX:F3},{dirZ:F3}) attackCenter=({packet.attackCenterX:F2},{packet.attackCenterZ:F2}) targetPos=({target.PosX:F2},{target.PosZ:F2})");
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
					string attackerType = session.EntityType == (int)Define.Layer.Player ? "플레이어" : "오몬";
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

		#region ScheduleManager 연동 메서드들 - 역할 분리를 위한 인터페이스

		/// <summary>
		/// ScheduleManager에서 호출하는 자동 공격 처리 메서드
		/// - 오브젝트의 애니메이션 타이밍에 맞춰 자동으로 공격 실행
		/// - 기존 AttackCheckToAttackResult 로직 재사용 (중복 제거)
		/// - AttackInfo 시트의 A + ownerSerial 구조 활용
		/// </summary>
		public void ProcessScheduledAttack(CommonSession attacker, int attackNumber)
		{
			//Console.WriteLine($"[Schedule] {attacker.SessionId} 자동 공격 실행 (A{attacker.SerialNumber}_{attackNumber})");
			// 가상의 AttackCheck 패킷 생성하여 기존 로직 재사용
			// attackNumber는 현재 AttackInfo 시트 구조상 사용되지 않음 (향후 확장 가능)
			var virtualAttackPacket = CreateVirtualAttackPacket(attacker, attackNumber);
			if (virtualAttackPacket != null)
			{
				// 기존 AttackCheckToAttackResult 메서드 재사용 (완성도 높은 로직 활용)
				AttackCheckToAttackResult(attacker, virtualAttackPacket);
			}
		}

		/// <summary>
		/// 오브젝트/몬스터 자동 공격을 위한 가상 AttackCheck 패킷 생성
		/// - ScheduleManager의 자동 공격을 기존 시스템과 호환되게 처리
		/// - 파이어베이스 AttackInfo 시트 구조에 맞게 attackSerial 생성 (A + ownerSerial + _공격번호)
		/// </summary>
		private C_EntityAttackCheck CreateVirtualAttackPacket(CommonSession attacker, int attackNumber)
		{
			// AttackInfoData 시트 참조 : O001, 1 → AO001_1  /  M000, 2 → AM000_2
			string attackSerial = $"A{attacker.SerialNumber}_{attackNumber}";
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
			
			if (attackInfo == null)
			{
				Console.WriteLine($"[Error] 공격 정보를 찾을 수 없습니다: {attackSerial} (Owner: {attacker.SerialNumber})");
				return null;
			}

			// NEW: 공격 범위 파싱 (AttackCheckToAttackResult와 동일한 방식)
			string[] rangeParts = attackInfo.range.Split('/');
			if (rangeParts.Length != 3)
			{
				Console.WriteLine($"[Error] 잘못된 range 형식: {attackInfo.range} (AttackSerial: {attackSerial})");
				return null;
			}

			// 공격 중심 = 본인 위치 + 앞(0.5m) + 약간 위(1m)
			double rad = attacker.RotationY * Math.PI / 180.0;
			float forwardX = (float)Math.Sin(rad);
			float forwardZ = (float)Math.Cos(rad);
			return new C_EntityAttackCheck {
				attackCenterX = attacker.PosX + forwardX * 0.5f,
				attackCenterY = attacker.PosY + 1f,
				attackCenterZ = attacker.PosZ + forwardZ * 0.5f,
				rotationY     = attacker.RotationY,
				attackSerial  = attackSerial,
			};
		}
		
		/// <summary>
		/// 사망 후 재생성 스케줄링 - 2단계 프로세스
		/// - 1단계: 5초 후 Leave 패킷 전송 (시체 사라짐)
		/// - 2단계: 추가 5초 후 재생성 (총 10초)
		/// - 오브젝트: 정확한 위치 재생성 / 몬스터: 원래 스폰 영역 내 랜덤 재생성
		/// </summary>
		private void ScheduleDeathAndRespawn(CommonSession deadSession)
		{
			// 사망한 엔티티의 원래 위치 정보 보존 (오브젝트용)
			var originalPosition = new { X = deadSession.PosX, Y = deadSession.PosY, Z = deadSession.PosZ };
			
			// 1단계 - 5초 후 Leave 패킷 전송 (시체 사라짐 효과)
			ScheduleManager.Instance.ScheduleTask(DateTime.UtcNow.AddSeconds(5), // 5초 후 시체 제거
				() => {
					// 모든 클라이언트에 Leave 패킷 전송 (시체 사라짐)
					SendEntityLeavePacket(deadSession);
					// 2단계 - 추가 5초 후 재생성 스케줄링 (타입별 재생성 방식)
					ScheduleRespawnAfterLeave(deadSession, originalPosition);
				},
				$"사망한 {deadSession.SerialNumber} 시체 제거 (5초 후)",
				deadSession.SessionId,
				SceneName
			);
		}

		/// <summary>
		///Leave 패킷 전송 - 사망한 엔티티 시체 제거
		/// - 모든 클라이언트에 엔티티 사라짐 알림
		/// - 사망 애니메이션 후 자연스러운 시체 제거 효과
		/// </summary>
		private void SendEntityLeavePacket(CommonSession deadSession)
		{
			//Console.WriteLine($"[GameRoom] {deadSession.SerialNumber} 시체 제거 - Leave 패킷 전송");
			// NEW: 모든 클라이언트에 Leave 패킷 브로드캐스트
			S_BroadcastEntityLeave leavePacket = new S_BroadcastEntityLeave {
				ID		   = deadSession.SessionId,
				entityType = deadSession.EntityType
			};
			Broadcast(leavePacket.Write());
		}

		/// <summary>
		/// Leave 후 재생성 스케줄링 - 2단계 재생성 프로세스
		/// - Leave 패킷 전송 후 5초 뒤 실제 재생성 실행
		/// - 원래 위치에서 정확히 재생성 (위치 정보 전달)
		/// - 총 재생성 시간: 10초 (사망 → 5초 → Leave → 5초 → 재생성)
		/// </summary>
		private void ScheduleRespawnAfterLeave(CommonSession deadSession, dynamic originalPosition)
		{
			// NEW: 2단계 - Leave 후 5초 뒤 재생성 (원래 위치 정보 포함)
			ScheduleManager.Instance.ScheduleTask(DateTime.UtcNow.AddSeconds(5), // 추가 5초 후 재생성
				() => { //SpawnManager에 재생성 위임 (원래 위치 정보 전달)
							SpawnManager.Instance.RespawnAtOriginalPosition(deadSession.SerialNumber, SceneName, originalPosition.X, originalPosition.Y, originalPosition.Z); },
				   $"사망한 {deadSession.SerialNumber} 재생성 실행 (Leave 후 5초)",
							deadSession.SessionId,
							SceneName);
		}
		
		#endregion
	}
}