using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Server.DB;

namespace Server
{
    /// <summary>
    /// 전투 관련 모든 로직을 담당하는 매니저
    /// - 충돌 판정, 히트 처리, 데미지 계산, 넉백 처리
    /// </summary>
    public class CombatManager
    {
        private GameRoom _gameRoom;
        
        public CombatManager(GameRoom gameRoom)
        {
            _gameRoom = gameRoom;
        }
        
        /// <summary>
        /// 공격 처리 메인 메서드
        /// </summary>
        public void ProcessAttack(CommonSession attacker, C_EntityAttackCheck packet)
        {
            // attackSerial을 통해 공격 정보 가져오기
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
            if (attackInfo == null)
                return;

            // 서버에서 플레이어 위치와 로테이션 정보 사용
            float playerX   = attacker.PosX;
            float playerY   = attacker.PosY;
            float playerZ   = attacker.PosZ;
            float playerRotY = attacker.RotationY;

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
                attackerID         = attacker.SessionId,
                attackerEntityType = attacker.EntityType,
                hitEffectSerial    = attackInfo.hitEffectSerial
            };
            
            // 데미지 계산 (기본 공격력 * 데미지 배수)
            float baseDamage       = attacker.Damage;
            float damageMultiplier = float.Parse(attackInfo.damageMultiplier);
            int   finalDamage      = (int)(baseDamage * damageMultiplier);
            attackResult.damage    = finalDamage;
        
            // 공격자 타입에 따른 타겟 필터링 및 처리
            var targets = GetAttackTargets(attacker.EntityType);
            foreach (var target in targets)
            {
                // 사망, 무적(일반/버프), HP 0 상태 => 넘어가기
                if (!target.Live || target.Invincibility || target.BuffInvincibility || target.CurrentHP <= 0)
                    continue;
                
                // 유효 체크 및 히트 로직 실행 (최종 결정된 공격 중심점 사용)
                if (IsInAttackRange(finalAttackX, finalAttackY, finalAttackZ, attacker.RotationY, attackInfo, target))
                    ProcessHit(attacker, target, finalDamage, finalAttackX, finalAttackY, finalAttackZ, attackResult);
            }
            
            // 공격 결과 브로드캐스트
            if (attackResult.entitys.Count > 0)
                _gameRoom.Broadcast(attackResult.Write());
        }

        #region 충돌 판정

        /// <summary>
        /// colliderType에 따른 충돌 판정 (BOX vs CIRCLE, CIRCLE vs CIRCLE)
        /// </summary>
        public bool IsInAttackRange(float attackCenterX, float attackCenterY, float attackCenterZ, float rotationY, AttackInfoData attackInfo, CommonSession target)
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

        /// <summary>
        /// BOX vs CIRCLE 충돌 판정 (기존 OBB 로직)
        /// </summary>
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

        /// <summary>
        /// CIRCLE vs CIRCLE 충돌 판정 (두 원의 거리 기반)
        /// </summary>
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

        #endregion

        #region 히트 처리

        /// <summary>
        /// 히트 처리 (체력 감소, 애니메이션, 위치 동기화, 결과 추가)
        /// </summary>
        public void ProcessHit(CommonSession attacker, CommonSession target, int damage, float attackCenterX, float attackCenterY, float attackCenterZ, S_BroadcastEntityAttackResult attackResult)
        {
            // 이미 사망 상태이거나 HP가 0인 타겟은 데미지 처리 제외
            if (!target.Live || target.CurrentHP <= 0)
                return;
            
            // 서버에서 타겟의 HP에 따라 상태 변경 및 브로드캐스트
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
                _gameRoom.ScheduleDeathAndRespawn(target);								  // 사망 후, 재생성 스케줄링. 10초 후 SpawnManager에 재생성 요청(=> 플레이어는 제외)
                    
                // 플레이어가 몬스터 사망시키면, EXP 관리 작업 진행
                // EXP가 레벨 필요 EXP를 넘어감 => 세션 정보 변경 및 브로드캐스트
                // EXP가 레벨 필요 EXP보다 적음 => 세션 정보 변경 및 단일 세그먼트 전송
                if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
                    _gameRoom.ProcessPlayerExpGain(attacker, target);
                    
                // 플레이어의 경우, 사망 후, DB에 저장된 씬의 이름을 Village로 변경.
                if (target.EntityType == (int)Define.Layer.Player && target is ClientSession playerSession)
                    _ = Program.DBManager._realTime.UpdateUserSceneAsync(playerSession.email, "Village");
            }
            
            // 세션 상태 변경 브로드캐스트
            _gameRoom.EntityInfoChange(target);
            
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
            _gameRoom.Broadcast(mv.Write());
        }

        #endregion

        #region 공격 타겟 관리

        /// <summary>
        /// 공격자 타입에 따른 타겟 목록 반환
        /// </summary>
        public List<CommonSession> GetAttackTargets(int attackerEntityType)
        {
            var targets = new List<CommonSession>();
                
            // 플레이어 공격 -> 오브젝트/몬스터 타겟
            if (attackerEntityType == (int)Define.Layer.Player)
                targets.AddRange(_gameRoom.GetCommonSessions().Where(s => s.EntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster));
            // 오브젝트/몬스터 공격 -> 플레이어 타겟
            else if (attackerEntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster)
                targets.AddRange(_gameRoom.GetCommonSessions().Where(s => s.EntityType == (int)Define.Layer.Player));
            
            return targets;
        }

        #endregion
    }
}
