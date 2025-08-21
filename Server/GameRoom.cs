using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
			// 서버의 Program.cs의 Main에서 정해진 초마다, 무한으로 루프하면서, 작동.
			// 락을 잡지 않는 이유 JobQueue를 사용하기 때문에
			var clientSessions = _commonSessions.OfType<ClientSession>().ToList(); // 컬렉션 수정 중 순회 문제 해결 => 안전한 복사본 생성
			foreach (ClientSession c in clientSessions)
				c.Send(_pendingList); // 다중 세그먼트 Send를 통해, _pendingList에 들어가 있는 모든 세그먼트 작업들을 보냄.
			
			_pendingList.Clear();
		}

		public void Broadcast(ArraySegment<byte> segment)
		{
			// 대기 목록에 추가(패킷을 바로 보내지 않고 일단 저장. 일정한 시간마다 비워 줌...)
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
				currentExp    = session.currentExp,
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
		
		// 공격자 타입에 따른 타겟 목록 반환
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
		
			// colliderType에 따른 충돌 판정 (BOX vs CIRCLE, CIRCLE vs CIRCLE)
	bool IsInAttackRange(float attackCenterX, float attackCenterY, float attackCenterZ, float rotationY, AttackInfoData attackInfo, CommonSession target)
	{
		// colliderType에 따른 분기 처리 (대소문자 구분 없음)
		string colliderType = attackInfo.colliderType?.ToUpper();
		
		if (colliderType == "BOX")
		{
			// BOX vs CIRCLE 충돌 판정 (기존 로직)
			return IsBoxVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, rotationY, attackInfo, target);
		}
		else if (colliderType == "CIRCLE")
		{
			// CIRCLE vs CIRCLE 충돌 판정 (새로운 로직)
			return IsCircleVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, attackInfo, target);
		}
		else
		{
			Console.WriteLine($"알 수 없는 colliderType: {colliderType} (원본: {attackInfo.colliderType})");
			// 기본값으로 BOX 처리 (기존 몬스터 호환성)
			return IsBoxVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, rotationY, attackInfo, target);
		}
	}

	// BOX vs CIRCLE 충돌 판정 (기존 OBB 로직)
	private bool IsBoxVsCircleCollision(float attackCenterX, float attackCenterY, float attackCenterZ, float rotationY, AttackInfoData attackInfo, CommonSession target)
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
			
			// range 정보 파싱 (예: "1.0 / 1.0 / 1.0")
			string[] rangeParts = attackInfo.range.Split('/');
			if (rangeParts.Length != 3)
			{
				Console.WriteLine($"잘못된 range 형식: {attackInfo.range}");
				return false;
			}

					float rangeX = float.Parse(rangeParts[0].Trim());
		float rangeZ = float.Parse(rangeParts[2].Trim()); 
		
		// Box 콜리더는 x, z 범위에 2를 곱해서 크기 조정
		float checkRangeX = rangeX * 2f;
		float checkRangeZ = rangeZ * 2f;
			
					// 공격 박스의 중심점과 크기(Y축 제외) - 최종 결정된 좌표 사용
		float centerX   = attackCenterX;
		float centerZ   = attackCenterZ;
			
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

	// CIRCLE vs CIRCLE 충돌 판정 (두 원의 거리 기반)
	private bool IsCircleVsCircleCollision(float attackCenterX, float attackCenterY, float attackCenterZ, AttackInfoData attackInfo, CommonSession target)
	{
		// 동작 원리:
		// 1. 공격 원의 중심과 타겟 원의 중심 사이의 거리 계산
		// 2. 거리가 (공격 반지름 + 타겟 반지름)보다 작으면 충돌
		// 
		// 장점:
		// - 계산이 매우 단순하고 빠름
		// - 원형 범위 공격(폭발, 방사형 공격)에 적합
		// - 직관적이고 이해하기 쉬움

		// range를 반지름으로 사용 (CIRCLE의 경우 단일 값)
		if (!float.TryParse(attackInfo.range, out float attackRadius))
		{
			Console.WriteLine($"CIRCLE colliderType의 잘못된 range 형식: {attackInfo.range}");
					return false;
		}

		// 타겟의 현재 위치와 반지름
		float targetX = target.PosX;
		float targetZ = target.PosZ;
		float targetRadius = target.Body_Size;

		// 두 원의 중심 사이의 거리 계산 (XZ 평면만, Y축 제외)
		float deltaX = targetX - attackCenterX;
		float deltaZ = targetZ - attackCenterZ;
		float distance = (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

		// 두 원이 겹치는지 확인 (거리 < 반지름의 합)
		// Circle 콜리더도 약간 범위를 늘려서 더 정확한 히트 판정
		float adjustedAttackRadius = attackRadius * 1.2f; // 20% 증가
		float totalRadius = adjustedAttackRadius + targetRadius;
		return distance <= totalRadius;
		}
		
		// 히트 처리 (체력 감소, 애니메이션, 위치 동기화, 결과 추가)
	void ProcessHit(CommonSession attacker, CommonSession target, int damage, float attackCenterX, float attackCenterY, float attackCenterZ, S_BroadcastEntityAttackResult attackResult)
		{
			// 이미 사망 상태이거나 HP가 0인 타겟은 데미지 처리 제외
			if (!target.Live || target.CurrentHP <= 0)
				return;
			
			// 서버에서 타겟의 HP에 따라 상태 변경 및 브로드케스트
			target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
			
			// 타겟 생존
			if (target.CurrentHP > 0)
			{
				target.AnimationId = (int)Define.Anime.Hit;																		  // 상태 변경
				ScheduleManager.Instance.SetAnimationState(target, Define.Anime.Hit);							      // ScheduleManager에 애니메이션 상태 등록 및 자동 전환 관리(=> 플레이어는 제외)
				if (target.AnimationId == (int)Define.Anime.Run) ScheduleManager.Instance.ProcessHitDuringRun(target.SessionId);  // 몬스터가 런 중에 히트를 당했으면, 이동한 시간 만큼 이동
			StartHitKnockBack(target, attackCenterX, attackCenterZ);											  // 히트 넉백 이동 시작 (몬스터/오브젝트) : 최종 결정된 공격 중심 좌표 사용
			}
			// 타겟 사망 (HP가 0 이하)
			else // target.CurrentHP <= 0
			{	
				target.Live        = false;										  // 상태 변경
				target.AnimationId = (int)Define.Anime.Death;					  // 상태 변경
				ScheduleManager.Instance.RemoveAnimationState(target.SessionId);  // ScheduleManager에서 애니메이션 상태 제거 (사망으로 더 이상 관리 불필요)
				ScheduleDeathAndRespawn(target);								  // 사망 후, 재생성 스케줄링. 10초 후 SpawnManager에 재생성 요청(=> 플레이어는 제외)
					
				// 플레이어가 몬스터 사망시키면, EXP 관리 작업 진행
				// EXP가 레벨 필요 EXP를 넘어감 => 세션 정보 변경 및 브로드케스트
				// EXP가 레벨 필요 EXP보다 적음 => 세션 정보 변경 및 단일 세그먼트 전송
				if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
					ProcessPlayerExpGain(attacker, target);
					
				// 플레이어의 경우, 사망 후, DB에 저장된 씬의 이름을 Village로 변경.
				if (target.EntityType == (int)Define.Layer.Player && target is ClientSession playerSession)
					_ = Program.DBManager._realTime.UpdateUserSceneAsync(playerSession.email, "Village");
			}
			
			// 세션 상태 변경 브로드케스트
			EntityInfoChange(target);
			
			// 서버 넉백과 동일한 방향 계산 (target -> hitCenter 의 반대)
		float rawDirX = target.PosX - attackCenterX;
		float rawDirZ = target.PosZ - attackCenterZ;
			float mag     = (float)Math.Sqrt(rawDirX * rawDirX + rawDirZ * rawDirZ);
			float dirX    = (mag > 0.0001f) ? rawDirX / mag : 0f;
			float dirZ    = (mag > 0.0001f) ? rawDirZ / mag : 0f;		
			var hitTarget = new S_BroadcastEntityAttackResult.Entity {
				targetID         = target.SessionId,
				targetEntityType = target.EntityType,
				hitMoveDirX      = dirX,
				hitMoveDirY      = 0,
				hitMoveDirZ      = dirZ,
			};
			attackResult.entitys.Add(hitTarget);
		}

		// 플레이어 경험치 획득 처리
		public void ProcessPlayerExpGain(CommonSession attacker, CommonSession deadMonster)
		{
			// 몬스터별 경험치 정보를 JSON에서 가져오기 (몬스터 시리얼 넘버 + 레벨 기반)
			int expGain = Program.DBManager.GetDropExp(deadMonster.SerialNumber, deadMonster.CurrentLevel);
			if (expGain <= 0)
				return; // 경험치가 0이면 처리하지 않음
			
			// 현재 레벨의 필요 경험치를 JSON에서 가져오기 (플레이어 시리얼 넘버 + 레벨 기반)
			int needEXP = Program.DBManager.GetNeedExp(attacker.SerialNumber, attacker.CurrentLevel);
			if (needEXP <= 0)
				return; // 필요 경험치 정보가 없으면 처리하지 않음
			
			// 다음 레벨에 대한 정보를 찾기
			CharacterInfoData newLevelInfo = Program.DBManager.GetCharacterInfo(attacker.SerialNumber, attacker.CurrentLevel + 1);
			if (newLevelInfo == null)
				return;	// 다음 레벨 정보가 없으면, 만렙인 상태
			
			// 레벨업(모두에게 브로드케스트)
			// 현재 Exp + 획득 Exp >= 현재 레벨의 needExp
			if (attacker.currentExp + expGain >= needEXP)
			{
				// 레벨업 처리 및 Exp 초기화
				attacker.CurrentLevel++;
				attacker.currentExp = 0;
				
				// 새로운 레벨의 스탯 정보로 업데이트
				if (int.TryParse(newLevelInfo.maxHp, out int newMaxHP))
				{
					attacker.MaxHP     = newMaxHP;
					attacker.CurrentHP = newMaxHP; // 레벨업 시 체력 완전 회복
				}
				if (int.TryParse(newLevelInfo.normalAttackDamage, out int newDamage))
				{
					attacker.Damage = newDamage;
				}
				if (float.TryParse(newLevelInfo.moveSpeed, out float newMoveSpeed))
				{
					attacker.moveSpeed = newMoveSpeed;
				}
				
				// EXP가 레벨 필요 EXP를 넘어감 => 세션 정보 변경 및 브로드케스트
				EntityInfoChange(attacker);
			}
			// Exp 증가(단일 Send)
			else
			{
				// Exp 증가
				attacker.currentExp += expGain;
				
				// EXP가 레벨 필요 EXP보다 적음 => 세션 정보 변경 및 단일 세그먼트 전송
				S_BroadcastEntityInfoChange expChange = new S_BroadcastEntityInfoChange {
					ID            = attacker.SessionId,
					entityType    = attacker.EntityType,
					currentExp    = attacker.currentExp,
					currentLevel  = attacker.CurrentLevel,
					currentHp     = attacker.CurrentHP,
					live          = attacker.Live,
					invincibility = attacker.Invincibility
				};
				attacker.Send(expChange.Write());
			}


			// // 세션에 현재 경험치 갱신
			// attacker.currentExp += expGain;
			//
			// // 레벨업 확인 및 처리
			// if (attacker.currentExp >= needEXP)
			// {
			// 	// 다음 레벨에 대한 정보를 찾기
			// 	CharacterInfoData newLevelInfo = Program.DBManager.GetCharacterInfo(attacker.SerialNumber, attacker.CurrentLevel + 1);
			// 	
			// 	// 다음 레벨에 대한 정보가 있는 경우
			// 	if (newLevelInfo != null)
			// 	{
			// 		// 레벨업 처리
			// 		attacker.CurrentLevel++;
			// 		attacker.currentExp = 0;
			//
			// 		// 새로운 레벨의 스탯 정보로 업데이트
			// 		if (int.TryParse(newLevelInfo.maxHp, out int newMaxHP))
			// 		{
			// 			attacker.MaxHP     = newMaxHP;
			// 			attacker.CurrentHP = newMaxHP; // 레벨업 시 체력 완전 회복
			// 		}
			// 		
			// 		if (int.TryParse(newLevelInfo.normalAttackDamage, out int newDamage))
			// 		{
			// 			attacker.Damage = newDamage;
			// 		}
			// 		
			// 		if (float.TryParse(newLevelInfo.moveSpeed, out float newMoveSpeed))
			// 		{
			// 			attacker.moveSpeed = newMoveSpeed;
			// 		}
			// 		
			// 		// EXP가 레벨 필요 EXP를 넘어감 => 세션 정보 변경 및 브로드케스트
			// 		EntityInfoChange(attacker);
			// 	}
			// 	// 다음 레벨에 대한 정보가 없는 경우
			// 	else
			// 	{
			// 		Console.WriteLine($"다음 레벨업 스탯 정보를 찾을 수 없습니다: {attacker.SerialNumber} 레벨 {attacker.CurrentLevel}");
			// 		// CharacterInfoData currentLevelInfo = Program.DBManager.GetCharacterInfo(attacker.SerialNumber, attacker.CurrentLevel);
			// 		// attacker.currentExp = int.Parse(currentLevelInfo.needEXP); // 최고값 유지만 하기
			// 	}
			// 	
			// }
			// // Exp Up 및 처리
			// else
			// {
			// 	// EXP가 레벨 필요 EXP보다 적음 => 세션 정보 변경 및 단일 세그먼트 전송
			// 	S_BroadcastEntityInfoChange expChange = new S_BroadcastEntityInfoChange {
			// 		ID            = attacker.SessionId,
			// 		entityType    = attacker.EntityType,
			// 		currentExp    = attacker.currentExp,
			// 		currentLevel  = attacker.CurrentLevel,
			// 		currentHp     = attacker.CurrentHP,
			// 		live          = attacker.Live,
			// 		invincibility = attacker.Invincibility
			// 	};
			// 	attacker.Send(expChange.Write());
			// }
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
					currentExp    = s.currentExp,
					currentGold   = s.CurrentGold,
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
				S_BroadcastEntityLeave selfLeavePacket = new S_BroadcastEntityLeave {
					ID         = session.SessionId,
					entityType = session.EntityType
				};
				clientSession.Send(selfLeavePacket.Write());
			}
			
			// (2) 방에 남아있는 다른 모든 클라이언트들에게는 "떠난다"는 사실을 브로드캐스트합니다.(이 패킷들은 _pendingList에 추가되었다가 다음 Flush() 타이밍에 전송됩니다.)
			S_BroadcastEntityLeave broadcastPacket = new S_BroadcastEntityLeave {
				ID         = session.SessionId,
				entityType = session.EntityType
			};
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
			
			// ScheduleManager에 애니메이션 상태 등록 및 자동 전환 관리(=> 플레이어는 제외)
			var animationType = (Define.Anime)packet.animationID;
			ScheduleManager.Instance.SetAnimationState(session, animationType);
			
			// 대쉬가 아닌 애니메이션이 들어오고, 세션이 무적인 경우
			if (session.Invincibility && (int)Define.Anime.Dash != packet.animationID)
			{
				session.Invincibility = false;	// 내 세션 정보 변경
				EntityInfoChange(session);		// 브로드케스트
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
			
			// ScheduleManager에 애니메이션 상태 등록 및 자동 전환 관리(=> 플레이어는 제외)
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
		
		// 공격 애니메이션 + 방향 + 공격넘버 처리
		public void AttackAnimation(CommonSession session, C_EntityAttackAnimation packet)
		{
			// 애니메이션 바꿔주고
			session.AnimationId = packet.animationID;
			
			// ScheduleManager에 공격 애니메이션 상태 등록 (플레이어 제외 / 공격 번호 포함, 자동 타이밍 관리)
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
		
		// 공격 유효 처리
		public void AttackCheckToAttackResult(CommonSession session, C_EntityAttackCheck packet)
		{	
			// attackSerial을 통해 공격 정보 가져오기
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
			if (attackInfo == null)
				return;

			// 서버에서 플레이어 위치와 로테이션 정보 사용
			float playerX   = session.PosX;
			float playerY   = session.PosY;
			float playerZ   = session.PosZ;
			float playerRotY = session.RotationY;

			// fixedCreatePos에 따른 최종 공격 중심점 결정
			float finalAttackX, finalAttackY, finalAttackZ;
		
			if (bool.Parse(attackInfo.fixedCreatePos))
			{
				// fixedCreatePos가 TRUE면 '서버 플레이어 위치 + createPos	오프셋' 사용
				var attackWorldPos = Extension.ComputeCreateWorldPos(playerX, playerY, playerZ, 
					playerRotY, attackInfo.createPos);
				finalAttackX = attackWorldPos.X;
				finalAttackY = attackWorldPos.Y;
				finalAttackZ = attackWorldPos.Z;
			
				// Console.WriteLine($"[서버 공격체크] AttackSerial: {packet.attackSerial}");
				// Console.WriteLine($"[서버 공격체크] 플레이어 위치: ({playerX:F3}, {playerY:F3}, {playerZ:F3})");
				// Console.WriteLine($"[서버 공격체크] 플레이어 로테이션: {playerRotY:F3}도");
				// Console.WriteLine($"[서버 공격체크] createPos 오프셋: {attackInfo.createPos}");
				// Console.WriteLine($"[서버 공격체크] 최종 공격 중심: ({finalAttackX:F3}, {finalAttackY:F3}, {finalAttackZ:F3})");
				// Console.WriteLine($"[서버 공격체크] 공격 범위(range): {attackInfo.range}");
				// Console.WriteLine($"[서버 공격체크] 콜라이더 타입: {attackInfo.colliderType}");
			}
			else
			{
				// fixedCreatePos가 FALSE면 클라이언트가 보낸 위치를 그대로 공격 중심점으로 사용
				finalAttackX = packet.createPosX;
				finalAttackY = packet.createPosY;
				finalAttackZ = packet.createPosZ;
			
				// Console.WriteLine($"[서버 공격체크] AttackSerial: {packet.attackSerial}");
				// Console.WriteLine($"[서버 공격체크] 클라이언트 보낸 위치: ({finalAttackX:F3}, {finalAttackY:F3}, {finalAttackZ:F3})");
				// Console.WriteLine($"[서버 공격체크] 공격 범위(range): {attackInfo.range}");
				// Console.WriteLine($"[서버 공격체크] 콜라이더 타입: {attackInfo.colliderType}");
			}
			
			// 어택결과 패킷 생성
			S_BroadcastEntityAttackResult attackResult = new S_BroadcastEntityAttackResult {
				attackerID         = session.SessionId,
				attackerEntityType = session.EntityType,
				effectSerial       = attackInfo.effectSerial
			};
			
			// 데미지 계산 (기본 공격력 * 데미지 배수)
			float baseDamage       = session.Damage;
			float damageMultiplier = float.Parse(attackInfo.damageMultiplier);
			int   finalDamage      = (int)(baseDamage * damageMultiplier);
			attackResult.damage    = finalDamage;
		
			// 공격자 타입에 따른 타겟 필터링 및 처리
			var targets = GetAttackTargets(session.EntityType);
			foreach (var target in targets)
			{
				// 사망, 무적(일반/버프), HP 0 상태 => 넘어가기
				if (!target.Live || target.Invincibility || target.BuffInvincibility || target.CurrentHP <= 0)
					continue;
				
				// 유효 체크 및 히트 로직 실행 (최종 결정된 공격 중심점 사용)
				if (IsInAttackRange(finalAttackX, finalAttackY, finalAttackZ, session.RotationY, attackInfo, target))
					ProcessHit(session, target, finalDamage, finalAttackX, finalAttackY, finalAttackZ, attackResult);
			}
			
			// 공격 결과 브로드캐스트
			if (attackResult.entitys.Count > 0)
				Broadcast(attackResult.Write());
		}
		
		// 채팅
		public void Chatting(CommonSession session, C_Chatting packet)
		{
			// 모두에게 알리기 위해, 대기 목록에 추가
			S_BroadcastChatting chatting = new S_BroadcastChatting {
				ID       = session.SessionId,
				contents = packet.contents
			};

			Broadcast(chatting.Write());
		}
		
		// 스킬 생성
		public void SkillCreate(CommonSession session, C_EntitySkillCreate packet)
		{
			// attackSerial 기반으로 createPos 파싱
			AttackInfoData atkInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
			if (atkInfo == null)
				return;

			// createPos를 이용하여 실제 월드 좌표 계산
			var skillWorldPos = Extension.ComputeCreateWorldPos(session.PosX, session.PosY, session.PosZ, 
				session.RotationY, atkInfo.createPos);

			// 스킬 타입에 따른 처리 분기
			string type      = atkInfo.type;
			float  duration  = float.Parse(atkInfo.duration);
			float  moveSpeed = float.Parse(atkInfo.moveSpeed);

			// fixedCreatePos에 따른 최종 스킬 생성 위치 결정
			float finalSkillX, finalSkillY, finalSkillZ;
		
			if (bool.Parse(atkInfo.fixedCreatePos))
			{
				// fixedCreatePos가 TRUE면 '내 위치 + createPos' 사용
				finalSkillX = skillWorldPos.X;
				finalSkillY = skillWorldPos.Y;
				finalSkillZ = skillWorldPos.Z;
			}
			else
			{
				// fixedCreatePos가 FALSE면 클라이언트가 보낸 createPosXYZ 사용
				finalSkillX = packet.createPosX;
				finalSkillY = packet.createPosY;
				finalSkillZ = packet.createPosZ;
			}

			// 브로드캐스트 패킷 생성 (x/y/z 형태의 문자열로 전송)
			S_BroadcastEntitySkillCreate skillPacket = new S_BroadcastEntitySkillCreate
			{
				ID				= session.SessionId,
				entityType      = session.EntityType,
				skillCreatePos  = $"{finalSkillX} / {finalSkillY} / {finalSkillZ}", // x / y / z 형태
				moveSpeed	    = moveSpeed,
				attackSerial    = packet.attackSerial,
				duration        = duration,
				type            = atkInfo.type
			};

			// 모든 클라이언트에게 스킬 생성 알림
			Broadcast(skillPacket.Write());

			// 스킬 타입별 서버 처리 (최종 결정된 좌표 사용)
			switch (type.ToLower())
			{
				case "move":
					HandleMoveSkill(session, finalSkillX, finalSkillY, finalSkillZ, atkInfo);
					break;
				case "continue":
					HandleContinueSkill(session, finalSkillX, finalSkillY, finalSkillZ, atkInfo);
					break;
				case "create": case "immediate":  // Immediate 타입도 Create와 동일하게 처리
					HandleCreateSkill(session, finalSkillX, finalSkillY, finalSkillZ, atkInfo);
					break;
				case "buff":
					HandleBuffSkill(session, atkInfo);
					break;
				default:
					Console.WriteLine($"알 수 없는 스킬 타입: {type}");
					break;
			}
	}
	
		#endregion
		
		# region 스킬 영역
		
		// Move 타입 스킬 처리 (이동하면서 경로상의 적들을 타격)
		private void HandleMoveSkill(CommonSession caster, float skillX, float skillY, float skillZ, AttackInfoData attackInfo)
		{
			float duration = float.Parse(attackInfo.duration);
			float moveSpeed = float.Parse(attackInfo.moveSpeed);
			bool penetration = bool.Parse(attackInfo.penetration);
			
			// 이동 거리 계산
			float totalDistance = moveSpeed * duration;
			
			// Move 스킬은 플레이어가 바라보는 방향으로 이동해야 함
			// Unity 좌표계: Z축이 앞방향, Y축 회전 기준
			double radians = caster.RotationY * Math.PI / 180.0;
			float dirX = (float)Math.Sin(radians);   // Unity: 오른쪽 방향
			float dirZ = (float)Math.Cos(radians);   // Unity: 앞방향
			
			Console.WriteLine($"[Move스킬] 캐스터 로테이션: {caster.RotationY:F3}도");
			Console.WriteLine($"[Move스킬] 이동 방향: dirX={dirX:F3}, dirZ={dirZ:F3}");
			Console.WriteLine($"[Move스킬] 시작 위치: ({skillX:F3}, {skillY:F3}, {skillZ:F3})");
			Console.WriteLine($"[Move스킬] 총 이동거리: {totalDistance:F3}, 지속시간: {duration:F3}초");
			
			// 이동 스킬 실시간 처리 (1초 동안 50ms마다 체크)
			ScheduleMoveSkillRealtime(caster, attackInfo.attackSerial, dirX, dirZ, totalDistance, penetration, 
							         skillX, skillY, skillZ, duration);
		}

		// Continue 타입 스킬 처리 (지속시간 동안 해당 위치에서 지속 데미지)
		private void HandleContinueSkill(CommonSession caster, float skillX, float skillY, float skillZ, AttackInfoData attackInfo)
		{
			// Continue 스킬은 attackTiming과 repeat을 AttackInfoData에서 직접 읽어옴
			// ScheduleContinueSkill에서 attackSerial로 데이터를 가져오므로 여기서는 파라미터만 전달
			ScheduleContinueSkill(caster, attackInfo.attackSerial, skillX, skillY, skillZ, 0f, 0, DateTime.UtcNow);
		}

		// Create 타입 스킬 처리 (즉시 해당 위치에 데미지 적용)
		private void HandleCreateSkill(CommonSession caster, float skillX, float skillY, float skillZ, AttackInfoData attackInfo)
		{
			// 즉시 공격 체크 실행 (최종 결정된 스킬 위치 사용)
			var virtualAttackCheck = new C_EntityAttackCheck
			{
				createPosX = caster.PosX,
				createPosY = caster.PosY,
				createPosZ = caster.PosZ,
				attackSerial  = attackInfo.attackSerial
			};
			
			AttackCheckToAttackResult(caster, virtualAttackCheck);
		}

		// Buff 타입 스킬 처리 (플레이어에게 버프 적용)
		private void HandleBuffSkill(CommonSession caster, AttackInfoData attackInfo)
		{
			string buffType = attackInfo.buffType;
			float duration = float.Parse(attackInfo.duration);
			
			Console.WriteLine($"[Buff] {caster.SessionId}에게 {buffType} 버프 적용 ({duration}초)");
			
			switch (buffType.ToLower())
			{
				case "invincibility":
					ApplyInvincibilityBuff(caster, duration);
					break;
				case "damage":
					ApplyDamageBuff(caster, duration);
					break;
				case "movespeed":
					ApplyMoveSpeedBuff(caster, duration);
					break;
				default:
					Console.WriteLine($"알 수 없는 buffType: {buffType}");
					break;
			}
		}

		// 무적 버프 적용
		private void ApplyInvincibilityBuff(CommonSession session, float duration)
		{
			// 기존 무적 상태 해제 (혹시 있다면)
			session.BuffInvincibility = false;
			
			// 새로운 무적 버프 적용
			session.BuffInvincibility = true;
			
			// 지속 시간 후 버프 해제 스케줄링
			DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
			ScheduleManager.Instance.ScheduleTask(endTime, () =>
			{
				session.BuffInvincibility = false;
				Console.WriteLine($"[Buff] {session.SessionId}의 무적 버프 해제");
			}, $"Invincibility Buff End", session.SessionId, SceneName);
		}

		// 데미지 버프 적용
		private void ApplyDamageBuff(CommonSession session, float duration)
		{
			// 기존 데미지 증가량 저장
			int originalDamage = session.Damage;
			
			// 데미지 2배 증가
			session.Damage = (int)(session.Damage * 2f);
			
			Console.WriteLine($"[Buff] {session.SessionId}의 데미지 증가: {originalDamage} → {session.Damage}");
			
			// 지속 시간 후 버프 해제 스케줄링
			DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
			ScheduleManager.Instance.ScheduleTask(endTime, () =>
			{
				session.Damage = originalDamage;
				Console.WriteLine($"[Buff] {session.SessionId}의 데미지 버프 해제: {session.Damage}");
			}, $"Damage Buff End", session.SessionId, SceneName);
		}

		// 이동속도 버프 적용  
		private void ApplyMoveSpeedBuff(CommonSession session, float duration)
		{
			// 기존 이동속도 저장
			float originalMoveSpeed = session.moveSpeed;
			
			// 이동속도 1.5배 증가
			session.moveSpeed *= 1.5f;
			
			Console.WriteLine($"[Buff] {session.SessionId}의 이동속도 증가: {originalMoveSpeed} → {session.moveSpeed}");
			
			// 지속 시간 후 버프 해제 스케줄링
			DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
			ScheduleManager.Instance.ScheduleTask(endTime, () =>
			{
				session.moveSpeed = originalMoveSpeed;
				Console.WriteLine($"[Buff] {session.SessionId}의 이동속도 버프 해제: {session.moveSpeed}");
			}, $"MoveSpeed Buff End", session.SessionId, SceneName);
		}

		// Move 스킬 실시간 처리 (attackSerial 데이터 기반)
		private void ScheduleMoveSkillRealtime(CommonSession caster, string attackSerial, float dirX, float dirZ, 
									          float totalDistance, bool penetration, 
									          float startX, float startY, float startZ, float duration)
		{
			HashSet<int> hitTargets = new HashSet<int>();
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
			
			// attackSerial에서 실제 데이터 가져오기
			float moveSpeedFromData = float.Parse(attackInfo.moveSpeed);
			float durationFromData  = float.Parse(attackInfo.duration);
			
			// 체크 간격은 고정 50ms, 스텝 크기는 데이터 기반
			float intervalSeconds   = 0.05f; // 50ms 간격 (고정)
			float stepSize          = moveSpeedFromData * intervalSeconds; 
			int   totalSteps        = (int)MathF.Round(durationFromData / intervalSeconds); // duration 기반 총 단계 수
			
			Console.WriteLine($"[Move스킬실시간] 속도:{moveSpeedFromData:F1}m/s, 지속:{durationFromData:F1}초, 간격:{intervalSeconds:F3}초, 스텝:{stepSize:F1}m, 총{totalSteps}단계");
			
			DateTime startTime = DateTime.UtcNow;
			
			// 50ms 정확한 간격 보장을 위해 전용 타이머 루프 사용 (ScheduleManager 미사용)
			System.Threading.Tasks.Task.Run(async () =>
			{
				for (int currentStep = 0; currentStep <= totalSteps; currentStep++)
				{
					float currentDistance = currentStep * stepSize;
					float currentX        = startX + dirX * currentDistance;
					float currentY        = startY;
					float currentZ        = startZ + dirZ * currentDistance;
					
					// 단일 스레드 보장을 위해 GameRoom 큐에 푸시
					this.Push(() =>
					{
						Console.WriteLine($"[Move실시간] 단계:{currentStep}/{totalSteps}, 거리:{currentDistance:F1}m, 위치:({currentX:F2},{currentY:F2},{currentZ:F2})");
						bool shouldContinue = CheckMoveSkillHit(caster, attackSerial, currentX, currentY, currentZ, hitTargets, penetration);
						if (!shouldContinue && currentStep < totalSteps)
						{
							float elapsedMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
							Console.WriteLine($"[Move실시간완료] 단계:{currentStep}/{totalSteps}, 경과:{elapsedMs:F0}ms / 목표:{durationFromData*1000:F0}ms (중단)");
						}
					});
					
					// 마지막 단계면 종료 로그
					if (currentStep == totalSteps)
					{
						float elapsedMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
						this.Push(() => Console.WriteLine($"[Move실시간완료] 단계:{currentStep}/{totalSteps}, 경과:{elapsedMs:F0}ms / 목표:{durationFromData*1000:F0}ms"));
						break;
					}
					
					await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
				}
			});
		}

		// Move 스킬의 히트 체크
		private bool CheckMoveSkillHit(CommonSession caster, string attackSerial, float currentX, float currentY, float currentZ, 
									  HashSet<int> hitTargets, bool penetration)
		{
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
			if (attackInfo == null) return false;
			
			// 가상 AttackCheck 패킷 생성 (스킬이 이동한 현재 위치 기준)
			var virtualAttackCheck = new C_EntityAttackCheck
			{
				createPosX = currentX,
				createPosY = currentY,
				createPosZ = currentZ,
				attackSerial = attackSerial
			};
			
			// 기존 공격 체크 로직 재사용하되, 이미 맞은 타겟은 제외
			var targets = GetAttackTargets(caster.EntityType);
			var newHits = new List<CommonSession>();
			
			foreach (var target in targets)
			{
				if (hitTargets.Contains(target.SessionId))
					continue; // 이미 맞은 타겟은 스킵
					
				// 사망, 무적(일반/버프), HP 0 상태 => 넘어가기
				if (!target.Live || target.Invincibility || target.BuffInvincibility || target.CurrentHP <= 0)
					continue;
					
				// 기존 충돌 판정 로직 사용 (colliderType 지원)
				if (IsInAttackRange(currentX, currentY, currentZ, caster.RotationY, attackInfo, target))
				{
					newHits.Add(target);
					hitTargets.Add(target.SessionId);
				}
			}
			
			// 새로운 히트가 있으면 데미지 처리
			if (newHits.Count > 0)
			{
				Console.WriteLine($"[Move스킬히트] 타겟 {newHits.Count}명 적중! 관통:{penetration}");
				// 기존 데미지 처리 로직 재사용
				ProcessSkillHits(caster, newHits, attackInfo);
			}
			
			// 올바른 로직: 관통이면 항상 계속, 관통 아니면 적을 안 맞춘 경우만 계속
			bool shouldContinue = penetration || newHits.Count == 0;
			Console.WriteLine($"[Move스킬로직] 적중:{newHits.Count}명, 관통:{penetration}, 계속여부:{shouldContinue}");
			return shouldContinue;
		}

		// Continue 스킬의 지속 데미지 스케줄링
		private void ScheduleContinueSkill(CommonSession caster, string attackSerial, float skillX, float skillY, float skillZ,
										  float interval, int repeatCount, DateTime startTime)
		{
			// attackSerial에서 실제 데이터 가져오기
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
			if (attackInfo == null) return;
			
			// Continue 스킬은 attackTiming 간격으로 repeat 횟수만큼 공격 체크
			float attackTiming = float.Parse(attackInfo.attackTiming); // 공격 간격 (초)
			int   repeat       = int.Parse(attackInfo.repeat);         // 반복 횟수
			
			Console.WriteLine($"[Continue스킬] 공격간격:{attackTiming:F3}초, 반복횟수:{repeat}회, 총시간:{attackTiming * repeat:F3}초");
			
			// 정확한 간격 보장을 위해 전용 타이머 루프 사용 (ScheduleManager 미사용)
			System.Threading.Tasks.Task.Run(async () =>
			{
				for (int currentCount = 0; currentCount < repeat; currentCount++)
				{
					// 단일 스레드 보장을 위해 GameRoom 큐에 푸시
					this.Push(() =>
					{
						Console.WriteLine($"[Continue실시간] 단계:{currentCount + 1}/{repeat}, 간격:{attackTiming:F3}초");
						CheckContinueSkillHit(caster, attackSerial, skillX, skillY, skillZ);
					});
					
					// 마지막 반복이면 종료
					if (currentCount == repeat - 1)
					{
						float elapsedMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
						this.Push(() => Console.WriteLine($"[Continue실시간완료] 총{repeat}회, 경과:{elapsedMs:F0}ms / 목표:{attackTiming * repeat * 1000:F0}ms"));
						break;
					}
					
					// attackTiming 간격만큼 대기 (밀리초 단위)
					await System.Threading.Tasks.Task.Delay((int)(attackTiming * 1000)).ConfigureAwait(false);
				}
			});
		}

		// Continue 스킬의 공격 체크
		private void CheckContinueSkillHit(CommonSession caster, string attackSerial, float skillX, float skillY, float skillZ)
		{
			AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
			if (attackInfo == null) return;
			
			// fixedCreatePos에 따른 최종 스킬 위치 결정
			float finalSkillX, finalSkillY, finalSkillZ;
			
			if (bool.Parse(attackInfo.fixedCreatePos))
			{
				// fixedCreatePos가 TRUE면 '스킬 생성 위치' 사용
				finalSkillX = skillX;
				finalSkillY = skillY;
				finalSkillZ = skillZ;
			}
			else
			{
				// fixedCreatePos가 FALSE면 클라이언트가 보낸 createPosXYZ 사용
				// 이 경우 skillX/Y/Z가 이미 클라이언트 위치로 설정되어 있음
				finalSkillX = skillX;
				finalSkillY = skillY;
				finalSkillZ = skillZ;
			}
			
			// Continue 스킬은 최종 결정된 위치에서 공격 체크
			var virtualAttackCheck = new C_EntityAttackCheck
			{
				createPosX   = finalSkillX,  // 최종 결정된 스킬 위치 사용
				createPosY   = finalSkillY,  // 최종 결정된 스킬 위치 사용  
				createPosZ   = finalSkillZ,  // 최종 결정된 스킬 위치 사용
				attackSerial = attackSerial
			};
			
			// 기존 AttackCheckToAttackResult 로직 재사용 (range, colliderType 지원)
			AttackCheckToAttackResult(caster, virtualAttackCheck);
		}

		// 스킬 히트 처리 (기존 로직 재사용)
		private void ProcessSkillHits(CommonSession attacker, List<CommonSession> targets, AttackInfoData attackInfo)
		{
			var hitList = new List<S_BroadcastEntityAttackResult.Entity>();
			
			foreach (var target in targets)
			{
				// 데미지 계산
				int damage = (int)(attacker.Damage * float.Parse(attackInfo.damageMultiplier));
				
				// 데미지 적용
				target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
				
				// 히트 방향 계산
				float hitDirX = target.PosX - attacker.PosX;
				float hitDirZ = target.PosZ - attacker.PosZ;
				float magnitude = (float)Math.Sqrt(hitDirX * hitDirX + hitDirZ * hitDirZ);
				
				if (magnitude > 0)
				{
					hitDirX /= magnitude;
					hitDirZ /= magnitude;
				}
				
				hitList.Add(new S_BroadcastEntityAttackResult.Entity
				{
					targetID = target.SessionId,
					targetEntityType = target.EntityType,
					hitMoveDirX = hitDirX,
					hitMoveDirY = 0f,
					hitMoveDirZ = hitDirZ
				});
				
				// 사망 처리 (ProcessHit과 동일한 로직)
				if (target.CurrentHP <= 0)
				{
					target.Live        = false;                                       // 상태 변경
					target.AnimationId = (int)Define.Anime.Death;                    // 상태 변경
					ScheduleManager.Instance.RemoveAnimationState(target.SessionId); // ScheduleManager에서 애니메이션 상태 제거
					ScheduleDeathAndRespawn(target);                                 // 사망 후 재생성 스케줄링
					
					// 플레이어가 몬스터 사망시키면, EXP 관리 작업 진행
					if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
						ProcessPlayerExpGain(attacker, target);
						
					// 플레이어의 경우, 사망 후, DB에 저장된 씬의 이름을 Village로 변경.
					if (target.EntityType == (int)Define.Layer.Player && target is ClientSession playerSession)
						_ = Program.DBManager._realTime.UpdateUserSceneAsync(playerSession.email, "Village");
				}
				else
				{
								// 생존 시 히트 애니메이션 처리
				target.AnimationId = (int)Define.Anime.Hit;
				ScheduleManager.Instance.SetAnimationState(target, Define.Anime.Hit);
			}
			
			// 세션 상태 변경 브로드케스트 (HP, Live 상태 등)
			EntityInfoChange(target);
			}
			
			// 공격 결과 브로드캐스트
			if (hitList.Count > 0)
			{
				S_BroadcastEntityAttackResult attackResult = new S_BroadcastEntityAttackResult
				{
					attackerID = attacker.SessionId,
					attackerEntityType = attacker.EntityType,
					damage = (int)(attacker.Damage * float.Parse(attackInfo.damageMultiplier)),
					effectSerial = attackInfo.effectSerial,
					entitys = hitList
				};
				
				Broadcast(attackResult.Write());
			}
			}
		
		# endregion
		
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
			createPosX = attacker.PosX,
			createPosY = attacker.PosY,
			createPosZ = attacker.PosZ,
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
			// 플레이어 제외
			// 플레이어는 사망 후, 마을로 돌아가기 버튼을 통해, 자기 자신이 직접 Leave를 요청.(= 마을로 돌아가기 버튼과 동일. 방에 들어왔을 때, HP가 0이면, 풀 충전하여 부활...)
			if (deadSession.EntityType == (int)Define.Layer.Player)
				return;
		
			// 1단계 - 5초 후 Leave 패킷 전송 (시체 사라짐 효과)
			ScheduleManager.Instance.ScheduleTask(DateTime.UtcNow.AddSeconds(5),					// 5초 후 시체 제거
											  () => { SendEntityLeavePacket(deadSession);		// 모든 클라이언트에 Leave 패킷 전송 (시체 사라짐)
														  ScheduleRespawnAfterLeave(deadSession);}, // 2단계 - 추가 5초 후 재생성 스케줄링 (타입별 재생성 방식)
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
			S_BroadcastEntityLeave leavePacket = new S_BroadcastEntityLeave {
				ID		   = deadSession.SessionId,
				entityType = deadSession.EntityType
			};
			Broadcast(leavePacket.Write());
		}
		
		/// <summary>
        /// Leave 후 재생성 스케줄링 - 2단계 재생성 프로세스 (OPTIMIZED)
        /// - Leave 패킷 전송 후 5초 뒤 실제 재생성 실행
        /// - mmNumber로 직접 원래 스폰 정보 참조 (거리 계산 불필요)
        /// - 총 재생성 시간: 10초 (사망 → 5초 → Leave → 5초 → 재생성)
        /// </summary>
        private void ScheduleRespawnAfterLeave(CommonSession deadSession)
        {
            // mmNumber로 직접 재생성 (위치 계산 불필요)
            ScheduleManager.Instance.ScheduleTask(DateTime.UtcNow.AddSeconds(5), // 추가 5초 후 재생성
									  () => { SpawnManager.Instance.RespawnByMmNumber(deadSession.MmNumber, SceneName); },			// SpawnManager에 재생성 위임 (mmNumber 기반 빠른 재생성)
										 $"사망한 {deadSession.SerialNumber}({deadSession.MmNumber}) 재생성 실행 (Leave 후 5초)",
                            					  deadSession.SessionId,
                            					  SceneName);
        }
		
		#endregion
	}
}