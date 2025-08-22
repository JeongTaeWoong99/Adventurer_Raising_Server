using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Server.DB;

namespace Server
{
    /// <summary>
    /// 스킬 관련 모든 로직을 담당하는 매니저
    /// - 스킬 생성, 타입별 처리, 스케줄링, 버프 관리
    /// </summary>
    public class SkillManager
    {
        private GameRoom _gameRoom;
        
        public SkillManager(GameRoom gameRoom)
        {
            _gameRoom = gameRoom;
        }
        
        /// <summary>
        /// 스킬 생성 메인 처리
        /// </summary>
        public void HandleSkillCreate(CommonSession session, C_EntitySkillCreate packet)
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
                ID				   = session.SessionId,
                entityType         = session.EntityType,
                skillCreatePos     = $"{finalSkillX} / {finalSkillY} / {finalSkillZ}", // x / y / z 형태
                moveSpeed	       = moveSpeed,
                attackEffectSerial = packet.attackSerial,
                duration           = duration,
                type               = atkInfo.type
            };

            // 모든 클라이언트에게 스킬 생성 알림
            _gameRoom.Broadcast(skillPacket.Write());

            // 스킬 타입별 서버 처리 (최종 결정된 좌표 사용)
            switch (type.ToLower())
            {
                case "move":
                    HandleMoveSkill(session, finalSkillX, finalSkillY, finalSkillZ, atkInfo);
                    break;
                case "continue":
                    Console.WriteLine($"[스킬생성] Continue 스킬 시작: {session.SessionId} -> {atkInfo.name} (ScheduleManager 사용)");
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

        #region 스킬 타입별 처리

        /// <summary>
        /// Move 타입 스킬 처리 (이동하면서 경로상의 적들을 타격)
        /// </summary>
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

        /// <summary>
        /// Continue 타입 스킬 처리 (지속시간 동안 해당 위치에서 지속 데미지)
        /// </summary>
        private void HandleContinueSkill(CommonSession caster, float skillX, float skillY, float skillZ, AttackInfoData attackInfo)
        {
            // ⚠️ 안전장치 추가: 스킬 실행 전 캐스터 상태 검증
            if (caster == null || !caster.Live || caster.CurrentHP <= 0)
            {
                Console.WriteLine($"[Continue스킬] 스킬 실행 불가: 캐스터가 유효하지 않음 - SessionId={caster?.SessionId}, Live={caster?.Live}, HP={caster?.CurrentHP}");
                return; // 스킬 실행 중단
            }
            
            Console.WriteLine($"[Continue스킬] 스킬 실행 시작: {caster.SessionId} -> {attackInfo.name}");
            
            // Continue 스킬은 attackTiming과 repeat을 AttackInfoData에서 직접 읽어옴
            // ScheduleContinueSkill에서 attackSerial로 데이터를 가져오므로 여기서는 파라미터만 전달
            ScheduleContinueSkill(caster, attackInfo.attackSerial, skillX, skillY, skillZ, 0f, 0, DateTime.UtcNow);
        }

        /// <summary>
        /// Create 타입 스킬 처리 (즉시 해당 위치에 데미지 적용)
        /// </summary>
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
            
            // GameRoom의 공격 처리 메서드 호출
            _gameRoom.ProcessAttack(caster, virtualAttackCheck);
        }

        /// <summary>
        /// Buff 타입 스킬 처리 (플레이어에게 버프 적용)
        /// </summary>
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

        #endregion

        #region 스킬 스케줄링

        /// <summary>
        /// Move 스킬 실시간 처리 (attackSerial 데이터 기반) - 스케줄매니저 통일
        /// </summary>
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
            
            // 스케줄매니저를 통해 50ms 간격으로 단계별 실행 스케줄링
            for (int currentStep = 0; currentStep <= totalSteps; currentStep++)
            {
                // ⚠️ 클로저 문제 해결: currentStep 값을 개별적으로 캡처
                int capturedStep = currentStep;
                DateTime executeTime = DateTime.UtcNow.AddMilliseconds(intervalSeconds * 1000 * capturedStep);
                
                ScheduleManager.Instance.ScheduleTask(executeTime, () =>
                {
                    // 메인 큐로 작업 전달 (실행하지 않고)
                    _gameRoom.Push(() =>
                    {
                        float currentDistance = capturedStep * stepSize;
                        float currentX        = startX + dirX * currentDistance;
                        float currentY        = startY;
                        float currentZ        = startZ + dirZ * currentDistance;
                        
                        Console.WriteLine($"[Move실시간] 단계:{capturedStep}/{totalSteps}, 거리:{currentDistance:F1}m, 위치:({currentX:F2},{currentY:F2},{currentZ:F2})");
                        bool shouldContinue = CheckMoveSkillHit(caster, attackSerial, currentX, currentY, currentZ, hitTargets, penetration);
                        
                        if (!shouldContinue && capturedStep < totalSteps)
                        {
                            Console.WriteLine($"[Move실시간완료] 단계:{capturedStep}/{totalSteps} (중단)");
                        }
                        
                        // 마지막 단계면 완료 로그
                        if (capturedStep == totalSteps)
                        {
                            Console.WriteLine($"[Move실시간완료] 단계:{capturedStep}/{totalSteps} 완료");
                        }
                    });
                }, $"MoveSkill_{attackSerial}_{capturedStep}", caster.SessionId, _gameRoom.SceneName);
            }
            
            Console.WriteLine($"[Move스킬실시간] {totalSteps}단계 스케줄 완료 - ScheduleManager 사용");
        }

        /// <summary>
        /// Continue 스킬의 지속 데미지 스케줄링
        /// </summary>
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
            
            // ⚠️ 수정: Task.Run 대신 ScheduleManager 사용
            // 모든 작업을 메인 큐를 통해 순차적으로 처리하여 데이터 경합 방지
            for (int i = 0; i < repeat; i++)
            {
                DateTime executeTime = DateTime.UtcNow.AddSeconds(attackTiming * i);
                
                ScheduleManager.Instance.ScheduleTask(executeTime, () =>
                {
                    // 메인 큐로 작업 전달 (실행하지 않고)
                    _gameRoom.Push(() =>
                    {
                        Console.WriteLine($"[Continue실시간] 단계:{i + 1}/{repeat}, 간격:{attackTiming:F3}초");
                        CheckContinueSkillHit(caster, attackSerial, skillX, skillY, skillZ);
                    });
                }, $"ContinueSkill_{attackSerial}_{i}", caster.SessionId, _gameRoom.SceneName);
            }
            
            Console.WriteLine($"[Continue스킬] {repeat}회 스케줄 완료 - ScheduleManager 사용");
        }

        #endregion

        #region 스킬 히트 체크

        /// <summary>
        /// Move 스킬의 히트 체크
        /// </summary>
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
            var targets = _gameRoom.GetAttackTargets(caster.EntityType);
            var newHits = new List<CommonSession>();
            
            foreach (var target in targets)
            {
                if (hitTargets.Contains(target.SessionId))
                    continue; // 이미 맞은 타겟은 스킵
                    
                // 사망, 무적(일반/버프), HP 0 상태 => 넘어가기
                if (!target.Live || target.Invincibility || target.BuffInvincibility || target.CurrentHP <= 0)
                    continue;
                    
                // 기존 충돌 판정 로직 사용 (colliderType 지원)
                if (_gameRoom.IsInAttackRange(currentX, currentY, currentZ, caster.RotationY, attackInfo, target))
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

        /// <summary>
        /// Continue 스킬의 공격 체크
        /// </summary>
        private void CheckContinueSkillHit(CommonSession caster, string attackSerial, float skillX, float skillY, float skillZ)
        {
            // ⚠️ 안전장치 추가: 캐스터 상태 검증
            if (caster == null || !caster.Live || caster.CurrentHP <= 0)
            {
                Console.WriteLine($"[Continue스킬] 캐스터가 유효하지 않음: SessionId={caster?.SessionId}, Live={caster?.Live}, HP={caster?.CurrentHP}");
                return; // 스킬 중단
            }
            
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
            _gameRoom.ProcessAttack(caster, virtualAttackCheck);
        }

        #endregion

        #region 스킬 히트 처리

        /// <summary>
        /// 스킬 히트 처리 (기존 로직 재사용)
        /// </summary>
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
                    _gameRoom.ScheduleDeathAndRespawn(target);                                 // 사망 후 재생성 스케줄링
                    
                    // 플레이어가 몬스터 사망시키면, EXP 관리 작업 진행
                    if(attacker.EntityType == (int)Define.Layer.Player && target.EntityType == (int)Define.Layer.Monster)
                        _gameRoom.ProcessPlayerExpGain(attacker, target);
                        
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
                
                // 세션 상태 변경 브로드캐스트 (HP, Live 상태 등)
                _gameRoom.EntityInfoChange(target);
            }
            
            // 공격 결과 브로드캐스트
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

        #region 버프 관리

        /// <summary>
        /// 무적 버프 적용
        /// </summary>
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
            }, $"Invincibility Buff End", session.SessionId, _gameRoom.SceneName);
        }

        /// <summary>
        /// 데미지 버프 적용
        /// </summary>
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
            }, $"Damage Buff End", session.SessionId, _gameRoom.SceneName);
        }

        /// <summary>
        /// 이동속도 버프 적용  
        /// </summary>
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
            }, $"MoveSpeed Buff End", session.SessionId, _gameRoom.SceneName);
        }

        #endregion
    }
}
