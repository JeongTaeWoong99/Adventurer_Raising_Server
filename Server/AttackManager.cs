using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Server.DB;

namespace Server
{
    /// <summary>
    /// 모든 공격 관련 로직을 담당하는 통합 매니저
    /// - 일반 공격, 스킬 공격 통합 처리
    /// - 충돌 판정, 히트 처리, 데미지 계산, 넉백 처리
    /// - 스킬 생성, 타입별 처리, 스케줄링, 버프 관리
    /// </summary>
    public class AttackManager
    {
        private GameRoom _gameRoom;
        
        public AttackManager(GameRoom gameRoom)
        {
            _gameRoom = gameRoom;
        }
        
        /// <summary>
        /// 통합 공격 처리 메인 메서드
        /// </summary>
        public void ProcessAttack(CommonSession attacker, C_EntityAttack packet)
        {
            // attackSerial을 통해 공격 정보 가져오기
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(packet.attackSerial);
            if (attackInfo == null)
                return;

            // 공격 타입에 따른 분기 처리
            string type = attackInfo.type;
            
            switch (type.ToLower())
            {
                case "immediate":
                    HandleImmediateAttack(attacker, packet, attackInfo);
                    break;
                case "move":
                    HandleMoveAttack(attacker, packet, attackInfo);
                    break;
                case "continue":
                    HandleContinueAttack(attacker, packet, attackInfo);
                    break;
                case "buff":
                    HandleBuffAttack(attacker, packet, attackInfo);
                    break;
                default:
                    Console.WriteLine($"알 수 없는 공격 타입: {type}");
                    break;
            }
        }

        #region 공격 타입별 처리

        /// <summary>
        /// Immediate 타입 공격 처리 (즉시 데미지 적용)
        /// </summary>
        private void HandleImmediateAttack(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // 공격 이펙트가 있으면 먼저 브로드캐스트
            if (!string.IsNullOrEmpty(attackInfo.attackEffectSerial) && attackInfo.attackEffectSerial != "X")
            {
                BroadcastSkillCreate(attacker, packet, attackInfo);
            }

            // 즉시 공격 체크 실행
            ProcessImmediateHitCheck(attacker, packet, attackInfo);
        }

        /// <summary>
        /// Move 타입 공격 처리 (이동하면서 경로상의 적들을 타격)
        /// </summary>
        private void HandleMoveAttack(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // fixedCreatePos에 따른 최종 스킬 생성 위치 결정
            float finalSkillX, finalSkillY, finalSkillZ;
            GetFinalAttackPosition(attacker, packet, attackInfo, out finalSkillX, out finalSkillY, out finalSkillZ);

            // 공격 이펙트 브로드캐스트
            BroadcastSkillCreate(attacker, packet, attackInfo);

            // Move 스킬 실시간 처리
            float duration = float.Parse(attackInfo.duration);
            float moveSpeed = float.Parse(attackInfo.moveSpeed);
            bool penetration = bool.Parse(attackInfo.penetration);
            
            // 이동 거리 계산
            float totalDistance = moveSpeed * duration;
            
            // 이동 방향 계산 (플레이어와 몬스터 좌표계 차이 고려)
            double radians = attacker.RotationY * Math.PI / 180.0;
            float dirX, dirZ;
            
            if (attacker.EntityType == (int)Define.Layer.Player)
            {
                // 플레이어: 클라이언트 좌표계 방식
                dirX = (float)Math.Sin(radians);
                dirZ = (float)Math.Cos(radians);
            }
            else
            {
                // 몬스터/오브젝트: 서버 좌표계 방식
                dirX = (float)Math.Cos(radians);
                dirZ = (float)Math.Sin(radians);
            }
            
            Console.WriteLine($"[Move공격] 캐스터 로테이션: {attacker.RotationY:F3}도");
            Console.WriteLine($"[Move공격] 이동 방향: dirX={dirX:F3}, dirZ={dirZ:F3} ({(attacker.EntityType == (int)Define.Layer.Player ? "플레이어" : "몬스터")} 좌표계)");
            Console.WriteLine($"[Move공격] 시작 위치: ({finalSkillX:F3}, {finalSkillY:F3}, {finalSkillZ:F3})");
            Console.WriteLine($"[Move공격] 총 이동거리: {totalDistance:F3}, 지속시간: {duration:F3}초");
            
            // 이동 공격 실시간 처리
            ScheduleMoveAttackRealtime(attacker, packet.attackSerial, dirX, dirZ, totalDistance, penetration, 
                                      finalSkillX, finalSkillY, finalSkillZ, duration);
        }

        /// <summary>
        /// Continue 타입 공격 처리 (지속시간 동안 해당 위치에서 지속 데미지)
        /// </summary>
        private void HandleContinueAttack(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // 캐스터 상태 검증
            if (attacker == null || !attacker.Live || attacker.CurrentHP <= 0)
            {
                Console.WriteLine($"[Continue공격] 공격 실행 불가: 캐스터가 유효하지 않음 - SessionId={attacker?.SessionId}, Live={attacker?.Live}, HP={attacker?.CurrentHP}");
                return;
            }

            // fixedCreatePos에 따른 최종 스킬 생성 위치 결정
            float finalSkillX, finalSkillY, finalSkillZ;
            GetFinalAttackPosition(attacker, packet, attackInfo, out finalSkillX, out finalSkillY, out finalSkillZ);

            // 공격 이펙트 브로드캐스트
            BroadcastSkillCreate(attacker, packet, attackInfo);
            
            Console.WriteLine($"[Continue공격] 공격 실행 시작: {attacker.SessionId} -> {attackInfo.name}");
            
            // Continue 공격 스케줄링
            ScheduleContinueAttack(attacker, packet.attackSerial, finalSkillX, finalSkillY, finalSkillZ);
        }

        /// <summary>
        /// Buff 타입 공격 처리 (플레이어에게 버프 적용)
        /// </summary>
        private void HandleBuffAttack(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // 공격 이펙트 브로드캐스트
            BroadcastSkillCreate(attacker, packet, attackInfo);

            string buffType = attackInfo.buffType;
            float duration = float.Parse(attackInfo.duration);
            
            Console.WriteLine($"[Buff] {attacker.SessionId}에게 {buffType} 버프 적용 ({duration}초)");
            
            switch (buffType.ToLower())
            {
                case "invincibility":
                    ApplyInvincibilityBuff(attacker, duration);
                    break;
                case "damage":
                    ApplyDamageBuff(attacker, duration);
                    break;
                case "movespeed":
                    ApplyMoveSpeedBuff(attacker, duration);
                    break;
                default:
                    Console.WriteLine($"알 수 없는 buffType: {buffType}");
                    break;
            }
        }

        #endregion

        #region 공통 유틸리티 메서드

        /// <summary>
        /// fixedCreatePos에 따른 최종 공격 위치 계산
        /// </summary>
        private void GetFinalAttackPosition(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo, 
                                          out float finalX, out float finalY, out float finalZ)
        {
            if (bool.Parse(attackInfo.fixedCreatePos))
            {
                // fixedCreatePos가 TRUE면 '내 위치 + createPos' 사용
                var attackWorldPos = Extension.ComputeCreateWorldPos(attacker.PosX, attacker.PosY, attacker.PosZ, 
                    attacker.RotationY, attackInfo.createPos);
                finalX = attackWorldPos.X;
                finalY = attackWorldPos.Y;
                finalZ = attackWorldPos.Z;
            }
            else
            {
                // fixedCreatePos가 FALSE면 클라이언트가 보낸 createPosXYZ 사용
                finalX = packet.createPosX;
                finalY = packet.createPosY;
                finalZ = packet.createPosZ;
            }
        }

        /// <summary>
        /// 스킬 생성 이펙트 브로드캐스트
        /// </summary>
        private void BroadcastSkillCreate(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // attackEffectSerial이 없거나 "X"면 이펙트 없음
            if (string.IsNullOrEmpty(attackInfo.attackEffectSerial) || attackInfo.attackEffectSerial == "X")
                return;

            // 최종 스킬 생성 위치 계산
            float finalSkillX, finalSkillY, finalSkillZ;
            GetFinalAttackPosition(attacker, packet, attackInfo, out finalSkillX, out finalSkillY, out finalSkillZ);

            float duration = float.Parse(attackInfo.duration);
            float moveSpeed = float.Parse(attackInfo.moveSpeed);

            // 브로드캐스트 패킷 생성
            S_BroadcastEntityAttackEffectCreate skillPacket = new S_BroadcastEntityAttackEffectCreate
            {
                ID                    = attacker.SessionId,
                entityType            = attacker.EntityType,
                attackEffectCreatePos = $"{finalSkillX} / {finalSkillY} / {finalSkillZ}",
                moveSpeed             = moveSpeed,
                attackEffectSerial    = attackInfo.attackEffectSerial,
                duration              = duration,
                type                  = attackInfo.type
            };

            // 모든 클라이언트에게 스킬 생성 알림
            _gameRoom.Broadcast(skillPacket.Write());
        }

        #endregion

        #region 즉시 공격 처리

        /// <summary>
        /// Immediate 타입 공격의 즉시 히트 체크
        /// </summary>
        private void ProcessImmediateHitCheck(CommonSession attacker, C_EntityAttack packet, AttackInfoData attackInfo)
        {
            // fixedCreatePos에 따른 최종 공격 중심점 결정
            float finalAttackX, finalAttackY, finalAttackZ;
            GetFinalAttackPosition(attacker, packet, attackInfo, out finalAttackX, out finalAttackY, out finalAttackZ);
            
            // 어택결과 패킷 생성
            S_BroadcastEntityAttackResult attackResult = new S_BroadcastEntityAttackResult
            {
                attackerID         = attacker.SessionId,
                attackerEntityType = attacker.EntityType,
                hitEffectSerial    = attackInfo.hitEffectSerial
            };
            
            // 데미지 계산
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
                
                // 충돌 판정 및 히트 처리
                if (IsInAttackRange(finalAttackX, finalAttackY, finalAttackZ, attacker.RotationY, attackInfo, target))
                    ProcessHit(attacker, target, finalDamage, finalAttackX, finalAttackY, finalAttackZ, attackResult);
            }
            
            // 공격 결과 브로드캐스트
            if (attackResult.entitys.Count > 0)
                _gameRoom.Broadcast(attackResult.Write());
        }

        #endregion

        #region 공격 스케줄링

        /// <summary>
        /// Move 공격 실시간 처리
        /// </summary>
        private void ScheduleMoveAttackRealtime(CommonSession caster, string attackSerial, float dirX, float dirZ, 
                                               float totalDistance, bool penetration, 
                                               float startX, float startY, float startZ, float duration)
        {
            HashSet<int> hitTargets = new HashSet<int>();
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
            
            float moveSpeedFromData = float.Parse(attackInfo.moveSpeed);
            float durationFromData  = float.Parse(attackInfo.duration);
            
            float intervalSeconds   = 0.02f; // 20ms 간격
            float stepSize          = moveSpeedFromData * intervalSeconds; 
            int   totalSteps        = (int)MathF.Round(durationFromData / intervalSeconds);
            
            Console.WriteLine($"[Move공격실시간] 속도:{moveSpeedFromData:F1}m/s, 지속:{durationFromData:F1}초, 간격:{intervalSeconds:F3}초, 스텝:{stepSize:F1}m, 총{totalSteps}단계");
            
            // 스케줄매니저를 통해 50ms 간격으로 단계별 실행 스케줄링
            for (int currentStep = 0; currentStep <= totalSteps; currentStep++)
            {
                int capturedStep = currentStep;
                DateTime executeTime = DateTime.UtcNow.AddMilliseconds(intervalSeconds * 1000 * capturedStep);
                
                ScheduleManager.Instance.ScheduleTask(executeTime, () =>
                {
                    _gameRoom.Push(() =>
                    {
                        float currentDistance = capturedStep * stepSize;
                        float currentX        = startX + dirX * currentDistance;
                        float currentY        = startY;
                        float currentZ        = startZ + dirZ * currentDistance;
                        
                        Console.WriteLine($"[Move실시간] 단계:{capturedStep}/{totalSteps}, 거리:{currentDistance:F1}m, 위치:({currentX:F2},{currentY:F2},{currentZ:F2})");
                        bool shouldContinue = CheckMoveAttackHit(caster, attackSerial, currentX, currentY, currentZ, hitTargets, penetration);
                        
                        if (!shouldContinue && capturedStep < totalSteps)
                        {
                            Console.WriteLine($"[Move실시간완료] 단계:{capturedStep}/{totalSteps} (중단)");
                        }
                        
                        if (capturedStep == totalSteps)
                        {
                            Console.WriteLine($"[Move실시간완료] 단계:{capturedStep}/{totalSteps} 완료");
                        }
                    });
                }, $"MoveAttack_{attackSerial}_{capturedStep}", caster.SessionId, _gameRoom.SceneName);
            }
            
            Console.WriteLine($"[Move공격실시간] {totalSteps}단계 스케줄 완료 - ScheduleManager 사용");
        }

        /// <summary>
        /// Continue 공격의 지속 데미지 스케줄링
        /// </summary>
        private void ScheduleContinueAttack(CommonSession caster, string attackSerial, float attackX, float attackY, float attackZ)
        {
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
            if (attackInfo == null) return;
            
            float attackTiming = float.Parse(attackInfo.attackTiming);
            int   repeat       = int.Parse(attackInfo.repeat);
            
            Console.WriteLine($"[Continue공격] 공격간격:{attackTiming:F3}초, 반복횟수:{repeat}회, 총시간:{attackTiming * repeat:F3}초");
            
            for (int i = 0; i < repeat; i++)
            {
                DateTime executeTime = DateTime.UtcNow.AddSeconds(attackTiming * i);
                
                ScheduleManager.Instance.ScheduleTask(executeTime, () =>
                {
                    _gameRoom.Push(() =>
                    {
                        Console.WriteLine($"[Continue실시간] 단계:{i + 1}/{repeat}, 간격:{attackTiming:F3}초");
                        CheckContinueAttackHit(caster, attackSerial, attackX, attackY, attackZ);
                    });
                }, $"ContinueAttack_{attackSerial}_{i}", caster.SessionId, _gameRoom.SceneName);
            }
            
            Console.WriteLine($"[Continue공격] {repeat}회 스케줄 완료 - ScheduleManager 사용");
        }

        #endregion

        #region 공격 히트 체크

        /// <summary>
        /// Move 공격의 히트 체크
        /// </summary>
        private bool CheckMoveAttackHit(CommonSession caster, string attackSerial, float currentX, float currentY, float currentZ, 
                                       HashSet<int> hitTargets, bool penetration)
        {
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
            if (attackInfo == null) return false;
            
            var targets = GetAttackTargets(caster.EntityType);
            var newHits = new List<CommonSession>();
            
            foreach (var target in targets)
            {
                if (hitTargets.Contains(target.SessionId))
                    continue;
                    
                if (!target.Live || target.Invincibility || target.BuffInvincibility || target.CurrentHP <= 0)
                    continue;
                    
                if (IsInAttackRange(currentX, currentY, currentZ, caster.RotationY, attackInfo, target))
                {
                    newHits.Add(target);
                    hitTargets.Add(target.SessionId);
                }
            }
            
            if (newHits.Count > 0)
            {
                Console.WriteLine($"[Move공격히트] 타겟 {newHits.Count}명 적중! 관통:{penetration}");
                ProcessAttackHits(caster, newHits, attackInfo);
            }
            
            bool shouldContinue = penetration || newHits.Count == 0;
            Console.WriteLine($"[Move공격로직] 적중:{newHits.Count}명, 관통:{penetration}, 계속여부:{shouldContinue}");
            return shouldContinue;
        }

        /// <summary>
        /// Continue 공격의 공격 체크
        /// </summary>
        private void CheckContinueAttackHit(CommonSession caster, string attackSerial, float attackX, float attackY, float attackZ)
        {
            if (caster == null || !caster.Live || caster.CurrentHP <= 0)
            {
                Console.WriteLine($"[Continue공격] 캐스터가 유효하지 않음: SessionId={caster?.SessionId}, Live={caster?.Live}, HP={caster?.CurrentHP}");
                return;
            }
            
            AttackInfoData attackInfo = Program.DBManager.GetAttackInfo(attackSerial);
            if (attackInfo == null) return;
            
            // Continue 공격은 지정된 위치에서 공격 체크
            var virtualAttackPacket = new C_EntityAttack
            {
                createPosX   = attackX,
                createPosY   = attackY,
                createPosZ   = attackZ,
                attackSerial = attackSerial
            };
            
            // 즉시 히트 체크 실행
            ProcessImmediateHitCheck(caster, virtualAttackPacket, attackInfo);
        }

        #endregion

        #region 충돌 판정 (CombatManager에서 가져옴)

        /// <summary>
        /// colliderType에 따른 충돌 판정
        /// </summary>
        public bool IsInAttackRange(float attackCenterX, float attackCenterY, float attackCenterZ, float rotationY, AttackInfoData attackInfo, CommonSession target)
        {
            string colliderType = attackInfo.colliderType?.ToUpper();
            
            if (colliderType == "BOX")
            {
                return IsBoxVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, rotationY, attackInfo, target);
            }
            else if (colliderType == "CIRCLE")
            {
                return IsCircleVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, attackInfo, target);
            }
            else
            {
                Console.WriteLine($"알 수 없는 colliderType: {colliderType} (원본: {attackInfo.colliderType})");
                return IsBoxVsCircleCollision(attackCenterX, attackCenterY, attackCenterZ, rotationY, attackInfo, target);
            }
        }

        /// <summary>
        /// BOX vs CIRCLE 충돌 판정
        /// </summary>
        private bool IsBoxVsCircleCollision(float attackCenterX, float attackCenterY, float attackCenterZ, float rotationY, AttackInfoData attackInfo, CommonSession target)
        {
            string[] rangeParts = attackInfo.range.Split('/');
            if (rangeParts.Length != 3)
            {
                Console.WriteLine($"잘못된 range 형식: {attackInfo.range}");
                return false;
            }

            float rangeX = float.Parse(rangeParts[0].Trim());
            float rangeZ = float.Parse(rangeParts[2].Trim()); 
            
            float checkRangeX = rangeX * 2f;
            float checkRangeZ = rangeZ * 2f;
                
            float centerX   = attackCenterX;
            float centerZ   = attackCenterZ;
                
            float targetX      = target.PosX;
            float targetZ      = target.PosZ;
            float targetRadius = target.Body_Size;
            
            // 타겟의 위치를 공격 박스의 로컬 좌표계로 변환
            float relativeX = targetX - centerX;
            float relativeZ = targetZ - centerZ;
            
            float cosY = (float)Math.Cos(-rotationY * Math.PI / 180.0f);
            float sinY = (float)Math.Sin(-rotationY * Math.PI / 180.0f);
            
            float localX = relativeX * cosY - relativeZ * sinY;
            float localZ = relativeX * sinY + relativeZ * cosY;
            
            float halfX = checkRangeX * 0.5f;
            float halfZ = checkRangeZ * 0.5f;
            
            float dx = Math.Max(0, Math.Abs(localX) - halfX);
            float dz = Math.Max(0, Math.Abs(localZ) - halfZ);
            
            float distanceSquared = dx * dx + dz * dz;
            bool hit = distanceSquared <= (targetRadius * targetRadius);
            
            // 디버그 로그 (A_M001_1 공격일 때만)
            if (attackInfo.attackSerial == "A_M001_1")
            {
                Console.WriteLine($"[Box충돌체크] {attackInfo.attackSerial}: 공격위치=({attackCenterX:F2},{attackCenterZ:F2}), 회전={rotationY:F1}도");
                Console.WriteLine($"[Box충돌체크] 타겟위치=({targetX:F2},{targetZ:F2}), 타겟반경={targetRadius:F3}");
                Console.WriteLine($"[Box충돌체크] Box크기=({checkRangeX:F2}x{checkRangeZ:F2}), 로컬좌표=({localX:F3},{localZ:F3})");
                Console.WriteLine($"[Box충돌체크] 거리제곱={distanceSquared:F6}, 타겟반경제곱={targetRadius * targetRadius:F6}, 히트={hit}");
            }
                
            return hit;
        }

        /// <summary>
        /// CIRCLE vs CIRCLE 충돌 판정
        /// </summary>
        private bool IsCircleVsCircleCollision(float attackCenterX, float attackCenterY, float attackCenterZ, AttackInfoData attackInfo, CommonSession target)
        {
            if (!float.TryParse(attackInfo.range, out float attackRadius))
            {
                Console.WriteLine($"CIRCLE colliderType의 잘못된 range 형식: {attackInfo.range}");
                return false;
            }

            float targetX = target.PosX;
            float targetZ = target.PosZ;
            float targetRadius = target.Body_Size;

            float deltaX = targetX - attackCenterX;
            float deltaZ = targetZ - attackCenterZ;
            float distance = (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

            float adjustedAttackRadius = attackRadius * 1.2f;
            float totalRadius = adjustedAttackRadius + targetRadius;
            bool hit = distance <= totalRadius;
            
            // 디버그 로그 (A_M001_1 공격일 때만)
            if (attackInfo.attackSerial == "A_M001_1")
            {
                Console.WriteLine($"[충돌체크] {attackInfo.attackSerial}: 공격위치=({attackCenterX:F2},{attackCenterZ:F2}), 타겟위치=({targetX:F2},{targetZ:F2})");
                Console.WriteLine($"[충돌체크] 거리={distance:F3}, 공격반경={attackRadius:F3}*1.2={adjustedAttackRadius:F3}, 타겟반경={targetRadius:F3}, 총반경={totalRadius:F3}, 히트={hit}");
            }
            
            return hit;
        }

        #endregion

        #region 히트 처리 (CombatManager에서 가져옴)

        /// <summary>
        /// 히트 처리 (체력 감소, 애니메이션, 위치 동기화, 결과 추가)
        /// </summary>
        public void ProcessHit(CommonSession attacker, CommonSession target, int damage, float attackCenterX, float attackCenterY, float attackCenterZ, S_BroadcastEntityAttackResult attackResult)
        {
            if (!target.Live || target.CurrentHP <= 0)
                return;
            
            target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
            
            if (target.CurrentHP > 0)
            {
                target.AnimationId = (int)Define.Anime.Hit;
                ScheduleManager.Instance.SetAnimationState(target, Define.Anime.Hit);
                if (target.AnimationId == (int)Define.Anime.Run) 
                    ScheduleManager.Instance.ProcessHitDuringRun(target.SessionId);
                StartHitKnockBack(target, attackCenterX, attackCenterZ);
            }
            else
            {	
                target.Live        = false;
                target.AnimationId = (int)Define.Anime.Death;
                ScheduleManager.Instance.RemoveAnimationState(target.SessionId);
                _gameRoom.ScheduleDeathAndRespawn(target);
                    
                if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
                    _gameRoom.ProcessPlayerExpGain(attacker, target);
                    
                if (target.EntityType == (int)Define.Layer.Player && target is ClientSession playerSession)
                    _ = Program.DBManager._realTime.UpdateUserSceneAsync(playerSession.email, "Village");
            }
            
            _gameRoom.EntityInfoChange(target);
            
            // 넉백 방향 계산
            float rawDirX = target.PosX - attackCenterX;
            float rawDirZ = target.PosZ - attackCenterZ;
            float mag     = (float)Math.Sqrt(rawDirX * rawDirX + rawDirZ * rawDirZ);
            float dirX    = (mag > 0.0001f) ? rawDirX / mag : 0f;
            float dirZ    = (mag > 0.0001f) ? rawDirZ / mag : 0f;		
            
            var hitTarget = new S_BroadcastEntityAttackResult.Entity
            {
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
        /// </summary>
        private void StartHitKnockBack(CommonSession target, float hitFromX, float hitFromZ)
        {
            if (target.moveSpeed <= 0 || target.hitLength <= 0)
                return;

            float dirX = target.PosX - hitFromX;
            float dirZ = target.PosZ - hitFromZ;
            float mag  = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (mag <= 0.0001f)
                return;
            dirX /= mag;
            dirZ /= mag;
            
            float moveX = dirX;
            float moveZ = dirZ;
            target.PosX += moveX / 5f;
            target.PosZ += moveZ / 5f;
            
            S_BroadcastEntityMove mv = new S_BroadcastEntityMove
            {
                ID              = target.SessionId,
                entityType      = target.EntityType,
                isInstantAction = true,			
                posX            = target.PosX,
                posY            = target.PosY,
                posZ            = target.PosZ
            };
            _gameRoom.Broadcast(mv.Write());
        }

        /// <summary>
        /// 스킬 히트 처리 (기존 SkillManager 로직)
        /// </summary>
        private void ProcessAttackHits(CommonSession attacker, List<CommonSession> targets, AttackInfoData attackInfo)
        {
            var hitList = new List<S_BroadcastEntityAttackResult.Entity>();
            
            foreach (var target in targets)
            {
                int damage = (int)(attacker.Damage * float.Parse(attackInfo.damageMultiplier));
                
                target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
                
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
                
                if (target.CurrentHP <= 0)
                {
                    target.Live        = false;
                    target.AnimationId = (int)Define.Anime.Death;
                    ScheduleManager.Instance.RemoveAnimationState(target.SessionId);
                    _gameRoom.ScheduleDeathAndRespawn(target);
                    
                    if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
                        _gameRoom.ProcessPlayerExpGain(attacker, target);
                        
                    if (target.EntityType == (int)Define.Layer.Player && target is ClientSession playerSession)
                        _ = Program.DBManager._realTime.UpdateUserSceneAsync(playerSession.email, "Village");
                }
                else
                {
                    target.AnimationId = (int)Define.Anime.Hit;
                    ScheduleManager.Instance.SetAnimationState(target, Define.Anime.Hit);
                }
                
                _gameRoom.EntityInfoChange(target);
            }
            
            if (hitList.Count > 0)
            {
                S_BroadcastEntityAttackResult attackResult = new S_BroadcastEntityAttackResult
                {
                    attackerID         = attacker.SessionId,
                    attackerEntityType = attacker.EntityType,
                    damage             = (int)(attacker.Damage * float.Parse(attackInfo.damageMultiplier)),
                    hitEffectSerial    = attackInfo.hitEffectSerial,
                    entitys            = hitList
                };
                
                _gameRoom.Broadcast(attackResult.Write());
            }
        }

        #endregion

        #region 공격 타겟 관리

        /// <summary>
        /// 공격자 타입에 따른 타겟 목록 반환
        /// </summary>
        public List<CommonSession> GetAttackTargets(int attackerEntityType)
        {
            var targets = new List<CommonSession>();
                
            if (attackerEntityType == (int)Define.Layer.Player)
                targets.AddRange(_gameRoom.GetCommonSessions().Where(s => s.EntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster));
            else if (attackerEntityType is (int)Define.Layer.Object or (int)Define.Layer.Monster)
                targets.AddRange(_gameRoom.GetCommonSessions().Where(s => s.EntityType == (int)Define.Layer.Player));
            
            return targets;
        }

        #endregion

        #region 버프 관리 (SkillManager에서 가져옴)

        /// <summary>
        /// 무적 버프 적용
        /// </summary>
        private void ApplyInvincibilityBuff(CommonSession session, float duration)
        {
            session.BuffInvincibility = false;
            session.BuffInvincibility = true;
            
            DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
            ScheduleManager.Instance.ScheduleTask(endTime, () =>
            {
                session.BuffInvincibility = false;
                Console.WriteLine($"[Buff] {session.SessionId}의 무적 버프 해제");
            }, $"Invincibility Buff End", session.SessionId, _gameRoom.SceneName);
        }

        /// <summary>
        /// 데미지 버프 적용
        /// </summary>
        private void ApplyDamageBuff(CommonSession session, float duration)
        {
            int originalDamage = session.Damage;
            session.Damage = (int)(session.Damage * 2f);
            
            Console.WriteLine($"[Buff] {session.SessionId}의 데미지 증가: {originalDamage} → {session.Damage}");
            
            DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
            ScheduleManager.Instance.ScheduleTask(endTime, () =>
            {
                session.Damage = originalDamage;
                Console.WriteLine($"[Buff] {session.SessionId}의 데미지 버프 해제: {session.Damage}");
            }, $"Damage Buff End", session.SessionId, _gameRoom.SceneName);
        }

        /// <summary>
        /// 이동속도 버프 적용  
        /// </summary>
        private void ApplyMoveSpeedBuff(CommonSession session, float duration)
        {
            float originalMoveSpeed = session.moveSpeed;
            session.moveSpeed *= 1.5f;
            
            Console.WriteLine($"[Buff] {session.SessionId}의 이동속도 증가: {originalMoveSpeed} → {session.moveSpeed}");
            
            DateTime endTime = DateTime.UtcNow.AddSeconds(duration);
            ScheduleManager.Instance.ScheduleTask(endTime, () =>
            {
                session.moveSpeed = originalMoveSpeed;
                Console.WriteLine($"[Buff] {session.SessionId}의 이동속도 버프 해제: {session.moveSpeed}");
            }, $"MoveSpeed Buff End", session.SessionId, _gameRoom.SceneName);
        }

        #endregion
    }
}
