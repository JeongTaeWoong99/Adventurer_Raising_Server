using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Server
{
    /// <summary>
    /// NEW: 시간 기반 작업 관리를 위한 클래스들
    /// - 특정 시간에 실행될 작업들을 관리
    /// - 사망 후 재생성, 애니메이션 전환 등에 사용
    /// </summary>
    public class ScheduledTask
    {
        public DateTime ExecuteTime { get; set; }    // 실행될 시간
        public Action   Task        { get; set; }    // 실행할 작업
        public string   Description { get; set; }    // 작업 설명 (디버깅용)
        public int      SessionId   { get; set; }    // 관련 세션 ID
        public string   RoomName    { get; set; }    // 관련 룸 이름
    }

    /// <summary>
    /// 각 엔티티의 애니메이션 상태를 관리하는 클래스
    /// - CommonSession의 애니메이션 데이터(hitLength, attackLength 등)를 직접 활용
    /// </summary>
    public class AnimationState
    {
        public Define.Anime  CurrentAnimation    { get; set; } = Define.Anime.Idle;  // 현재 애니메이션 타입
        public DateTime      AnimationStartTime  { get; set; }                       // 애니메이션 시작 시간
        public bool          AttackTriggered     { get; set; } = false;              // 공격 데미지 발생 여부
        public int           CurrentAttackNumber { get; set; } = 1;                  // 현재 공격 번호 (1, 2, 3) - CommonSession.attackLength[번호] 사용
        
        // 트랩용 반복 공격 시스템
        public DateTime LastAttackTime    { get; set; }          // 마지막 공격 시간 (트랩 쿨다운 계산용)
        public bool     IsRepeatingAttack { get; set; } = false; // 반복 공격 여부 (O001 Spike Trap 등)

        // Run 이동한 만큼 동기화
        public float MoveDirectionX { get; set; }
        public float MoveDirectionZ { get; set; }
        public bool HasMoveDirection { get; set; }
    }

    /// <summary>
    /// 시간 기반 작업 스케줄링 관리 매니저
    /// - GameRoom의 시간 관련 작업을 분리하여 단일 책임 원칙 적용
    /// - 100ms 타이머로 실시간 애니메이션 상태 관리
    /// - 파이어베이스 데이터(hitLength, attackLength, attackTiming) 기반 동작
    /// </summary>
    public class ScheduleManager
    {
        private static ScheduleManager _instance = new ScheduleManager();
        public static ScheduleManager Instance { get { return _instance; } }

        private List<ScheduledTask>             _scheduledTasks  = new List<ScheduledTask>();             // 스케줄된 작업 목록
        private Dictionary<int, AnimationState> _animationStates = new Dictionary<int, AnimationState>(); // SessionId별 애니메이션 상태
        private Lock                            _lock            = new Lock();                            // 멀티스레드 안전성을 위한 락
        private Timer                           _lifecycleTimer;                                          // 100ms 주기 타이머
        
        private ScheduleManager()
        {
            InitializeTimer();
        }

        #region 무한 반복 체크 구역

        /// <summary>
        /// 100ms 주기 타이머 초기화
        /// - 모든 애니메이션과 스케줄 작업을 실시간으로 체크
        /// </summary>
        private void InitializeTimer()
        {
            _lifecycleTimer = new Timer(100);     // 100ms마다 체크 (게임 반응성 확보)
            _lifecycleTimer.Elapsed += OnTimerElapsed;  // 시간이 경과 하면, 등록된 스케줄 실행(무한 반복)
            _lifecycleTimer.Start();                    // 작동(내장 메서드)
        }

        /// <summary>
        /// 타이머 이벤트 핸들러 - 핵심 로직 실행
        /// - 1. 스케줄된 작업 실행 (사망 후 재생성 등)
        /// - 2. 애니메이션 상태 업데이트 (Hit → Idle, Attack → Idle 전환)
        /// </summary>
        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            
            lock (_lock)
            {
                // 1. 스케줄된 작업 실행 (시간 도달한 작업들)
                ExecuteScheduledTasks(now);
                
                // 2. 애니메이션 상태 업데이트 (실시간 애니메이션 관리)
                UpdateAnimationStates(now);
            }
        }

        /// <summary>
        /// 스케줄된 작업 실행
        /// - 시간이 도달한 작업들을 실행하고 목록에서 제거
        /// - 사망 후 재생성, 버프/디버프 해제 등에 사용
        /// </summary>
        private void ExecuteScheduledTasks(DateTime now)
        {
            var tasksToExecute = _scheduledTasks.Where(t => t.ExecuteTime <= now).ToList();
            
            foreach (var task in tasksToExecute)
            {
                try
                {
                    task.Task?.Invoke();
                    //Console.WriteLine($"[Schedule] 실행: {task.Description}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Schedule] 오류: {task.Description}, {ex.Message}");
                }
            }
            
            // 실행된 작업들을 목록에서 제거
            _scheduledTasks.RemoveAll(t => tasksToExecute.Contains(t));
        }

        /// <summary>
        /// 애니메이션 상태 업데이트 - 실시간 애니메이션 관리
        /// - 각 엔티티의 애니메이션 진행 상황을 체크
        /// </summary>
        private void UpdateAnimationStates(DateTime now)
        {
            var animationsToUpdate = _animationStates.ToList();
            
            foreach (var kvp in animationsToUpdate)
            {
                int sessionId = kvp.Key;
                var animState = kvp.Value;
                
                // 세션 찾기 (모든 룸에서 검색)
                var session = FindSessionById(sessionId);
                if (session == null) continue;

                // 애니메이션 타입별 처리
                switch (animState.CurrentAnimation)
                {
                    case Define.Anime.Idle:
                        // 몬스터류 => 플레이어 찾기
                        if (session.SerialNumber.StartsWith("M") && session.findRadius > 0)
                            CheckPlayerDetection(session, animState, now);
                        // 트랩류 => 공격 반복
                        if (animState.IsRepeatingAttack)
                            CheckPossibleAttack(session, animState, now);
                        break;
                    case Define.Anime.Run:
                        // 정상 Run 이동한 만큼 이동(저장된 방향 x 시간)
                        RunTimePosMove(session, animState, now);
                        // 몬스터류 => 플레이어 찾기
                        if (session.SerialNumber.StartsWith("M") && session.findRadius > 0)
                            CheckPlayerDetection(session, animState, now);
                        break;
                    case Define.Anime.Hit:
                        CheckHitAnimationEnd(session, animState, now);          // Hit 애니메이션 종료 체크(HIT -> IDLE)
                        break;
                    case Define.Anime.Attack:
                        CheckAttackAnimationProgress(session, animState, now);  // Attack 진행 상황 체크(ATTACK -> IDLE)
                        break;
                }
            }
        }
        
        #endregion

        #region 몬스터 기능
        
        // Run 이동한 만큼 동기화
        // 정상적으로 다음 애니메이션 체크 때, Run 상태이면, 기존 디렉션 만큼 위치를 옮겨줌.
        // 히트 같이, 정상적으로 100ms를 지나지 못하고, Run이 끝나면, 이동 시간 만큼 이동 시켜준다.
        private void RunTimePosMove(CommonSession monster, AnimationState animState, DateTime now)
        {
            // 몬스터만 이동 처리
            if (!monster.SerialNumber.StartsWith("M"))
                return;

            // 저장된 이동 방향이 있는지 확인
            if (!animState.HasMoveDirection)
                return;

            // 100ms 동안 이동할 거리 계산
            float deltaTime    = 0.1f; // 100ms = 0.1초
            float moveDistance = monster.moveSpeed * deltaTime;

            // 새로운 위치 계산
            float newPosX = monster.PosX + (animState.MoveDirectionX * moveDistance);
            float newPosZ = monster.PosZ + (animState.MoveDirectionZ * moveDistance);

            // 위치 업데이트
            monster.PosX = newPosX;
            monster.PosZ = newPosZ;

            // 방향에 따른 회전 각도 계산
            float angleRad = (float)Math.Atan2(animState.MoveDirectionX, animState.MoveDirectionZ);
            float angleDeg = angleRad * 180.0f / (float)Math.PI;
            monster.RotationY = angleDeg;

            Console.WriteLine($"[몬스터 이동] {monster.SerialNumber}({monster.SessionId}) " +
                            $"위치: ({monster.PosX:F2}, {monster.PosZ:F2}), " +
                            $"방향: ({animState.MoveDirectionX:F3}, {animState.MoveDirectionZ:F3}), " +
                            $"각도: {angleDeg:F1}도");
        }    
        
        /// <summary>
        /// 몬스터 플레이어 탐지 시스템
        /// - findRadius 범위 내에 있는 플레이어 탐지
        /// - 가장 가까운 플레이어를 타겟으로 설정
        /// - 방향 벡터 계산 및 콘솔 출력
        /// </summary>
        private void CheckPlayerDetection(CommonSession monster, AnimationState animState, DateTime now)
        {
            // 같은 룸에서 살아있는 플레이어들 찾기
            var alivePlayers = monster.Room._commonSessions
                                               .Where(s => s.EntityType == (int)Define.Layer.Player && s.Live)
                                               .ToList();
            
            // 살아있는 플레이어 없음 + Idle이 아님 => IDLE 전환 및 전파
            if (alivePlayers.Count == 0 && animState.CurrentAnimation != 0)
            {
                // 서버 모든 상태를 IDLE로 맞춰서 전환
                // 클라 IDLE 상태 브로드케스트(방에 플레이어가 없다면, 전파하지 않음 => 죽었지만, 방에 있으면, 전파)
                TransitionToIdle(monster, animState);
            }
            else
            {
                // 범위 안 플레이어 찾기
                float         detectionRadius = monster.findRadius;
                CommonSession targetPlayer    = null;
                float         closestDistance = float.MaxValue;
                
                // 가장 가까운 플레이어 찾기
                foreach (var player in alivePlayers)
                {
                    // 2D 거리 계산 (X, Z 좌표 사용)
                    float deltaX   = player.PosX - monster.PosX;
                    float deltaZ   = player.PosZ - monster.PosZ;
                    float distance = (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

                    // 탐지 범위 내에 있고, 가장 가까운 플레이어인지 확인
                    if (distance <= detectionRadius && distance < closestDistance)
                    {
                        closestDistance = distance;
                        targetPlayer    = player;
                    }
                }

                // 범위 안 플레이어를 발견
                if (targetPlayer != null)
                {
                    // 방향 벡터 계산 (몬스터 -> 플레이어)
                    float dirX = targetPlayer.PosX - monster.PosX;
                    float dirZ = targetPlayer.PosZ - monster.PosZ;
                    
                    // 랜덤 오프셋 추가 (직선이 아닌 살짝 비껴서 이동)
                    Random rand = new Random();
                    float randomOffsetX = (float)(rand.NextDouble() * 1 - 0.5); // -0.5 ~ 0.5
                    float randomOffsetZ = (float)(rand.NextDouble() * 1 - 0.5); // -0.5 ~ 0.5
                    dirX += randomOffsetX;
                    dirZ += randomOffsetZ;
                    
                    // 방향 벡터 정규화
                    float magnitude = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (magnitude > 0)
                    {
                        dirX /= magnitude;
                        dirZ /= magnitude;
                    }

                    // 각도 계산 (라디안 -> 도)
                    float angleRad = (float)Math.Atan2(dirZ, dirX);
                    float angleDeg = angleRad * (180.0f / (float)Math.PI);

                    //Console.WriteLine($"[몬스터 탐지] {monster.SerialNumber}({monster.SessionId}) -> 플레이어({targetPlayer.SessionId}) " + $"거리: {closestDistance:F2}m, 방향: ({dirX:F3}, {dirZ:F3}), 각도: {angleDeg:F1}도");
                    
                    // 이전 방향과 비교하여 변경되었는지 확인
                    float threshold        = 0.001f; // 방향 변경 임계값
                    bool  directionChanged = Math.Abs(animState.MoveDirectionX - dirX) > threshold || Math.Abs(animState.MoveDirectionZ - dirZ) > threshold;
                    bool  wasIdle          = animState.CurrentAnimation == Define.Anime.Idle;
                    
                    // 이동 방향 설정
                    animState.MoveDirectionX   = dirX;
                    animState.MoveDirectionZ   = dirZ;
                    animState.HasMoveDirection = true;
                    
                    // 몬스터 회전 설정
                    monster.RotationY = angleDeg;
                    
                    // Idle 상태에서만 Run으러 전환 및 전파
                    if (wasIdle)
                    {
                        animState.CurrentAnimation   = Define.Anime.Run;
                        animState.AnimationStartTime = DateTime.UtcNow;
                        monster.AnimationId          = 1;
                        
                        // Run 애니메이션 브로드캐스트
                        monster.Room?.Push(() =>
                        {
                            var runAnimPacket = new S_BroadcastEntityAnimation {
                                ID          = monster.SessionId,
                                entityType  = monster.EntityType,
                                animationID = 1
                            };
                            monster.Room.Broadcast(runAnimPacket.Write());
                        });
                        
                        //Console.WriteLine($"[몬스터 추적 시작] {monster.SerialNumber}({monster.SessionId}) -> Run 애니메이션 시작");
                    }
                    
                    // 방향이 전환된 경우에 브로드캐스트
                    if (directionChanged)
                    {
                        // 새로운 방향 브로드캐스트
                        monster.Room?.Push(() =>
                        {
                            var movePacket = new S_BroadcastEntityMove {
                                ID              = monster.SessionId,
                                entityType      = monster.EntityType,
                                isInstantAction = false,
                                posX            = dirX, 
                                posY            = 0,
                                posZ            = dirZ
                            };
                            monster.Room.Broadcast(movePacket.Write());
                            
                            //Console.WriteLine($"[몬스터 방향 변경] {monster.SerialNumber}({monster.SessionId}) -> 새로운 방향 브로드캐스트");
                        });
                    }
                }
                // 범위 안 플레이어를 없음
                else
                {
                    TransitionToIdle(monster, animState);
                }
            }
        }        
        
        #endregion

        #region 오브젝트 기능

        

        #endregion

        #region 공통 기능
        
        /// <summary>
        /// Hit 애니메이션 종료 체크
        /// - CommonSession의 hitLength 값을 직접 사용해서 체크
        /// - Hit 시간이 끝나면 자동으로 Idle로 전환
        /// </summary>
        private void CheckHitAnimationEnd(CommonSession session, AnimationState animState, DateTime now)
        {
            var hitLength = session.hitLength;                                  // 파이어베이스에서 가져온 데이터 직접 활용
            var elapsed  = (now - animState.AnimationStartTime).TotalSeconds;  // 시간이 얼마나 지났는지 체크(현재시간 - 시작시간 = 작동시간)
            
            if (elapsed >= hitLength)
            {
                // 플레이어 제외
                // 플레이어는 클라이언트 측에서 애니메이션 전환을 관리하므로, 서버에서 Idle 상태로 강제 전환하지 않는다.
                if (session.EntityType == (int)Define.Layer.Player)
                    return;

                TransitionToIdle(session, animState);
            }
        }
        
        /// <summary>
        /// 가능 공격 체크
        /// - 반복 오브젝트는 쿨타임 기반 반복 공격 발생
        /// - 몬스터는 공격 가능 1~3 중, 범위 + 쿨타임 체크해서, 발생
        /// </summary>
        private void CheckPossibleAttack(CommonSession session, AnimationState animState, DateTime now)
        {
            // 오브젝트(트랩)은 공격1 반복
            if (session.SerialNumber.StartsWith("O"))
            {
                string attackSerial = $"A{session.SerialNumber}_1";
                AttackInfoData info = Program.DBManager.GetAttackInfo(attackSerial);
                if (info == null) return;

                float.TryParse(info.coolTime, out float cooldown);
                if (cooldown <= 0) return;

                DateTime lastTime = DateTime.MinValue;
                session.LastAttackTimes.TryGetValue(attackSerial, out lastTime);

                if ((now - lastTime).TotalSeconds >= cooldown)
                {
                    StartAttackAnimation(session, animState, 1);
                }
                return;
            }

            // 몬스터: 여러 공격 중 조건 만족하는 것 선택
            if (session.SerialNumber.StartsWith("M"))
            {
                List<int> candidateAttacks = new List<int>();
                for (int i = 1; i <= 3; i++)
                {
                    string attackSerial = $"A{session.SerialNumber}_{i}";
                    var info = Program.DBManager.GetAttackInfo(attackSerial);
                    if (info == null) continue;
                    
                    float.TryParse(info.coolTime, out float cooldown);
                    if (cooldown <= 0) continue;

                    DateTime lastTime = DateTime.MinValue;
                    session.LastAttackTimes.TryGetValue(attackSerial, out lastTime);
                    if ((now - lastTime).TotalSeconds >= cooldown)
                        candidateAttacks.Add(i);
                }

                if (candidateAttacks.Count == 0) 
                    return; // 쿨다운 중

                // 거리 기반 필터링은 GameRoom에서 처리하므로, 여기서는 랜덤 선택만
                // TODO : 추후, 몬스터가 따라가기 타겟을 선택했으면,
                // TODO : 일정 시간마다, 타겟 위치 + 범위에 선택된 공격의 범위를 비교하여,
                // TODO : 유효하면, 공격을 발생시키도록 함....
                Random rand = new Random();
                int attackNumber = candidateAttacks[rand.Next(candidateAttacks.Count)];
                StartAttackAnimation(session, animState, attackNumber);
            }
        }    
        
        // Attack 애니메이션 시작
        private void StartAttackAnimation(CommonSession session, AnimationState animState, int attackNumber = 1)
        {
            animState.CurrentAnimation    = Define.Anime.Attack;
            animState.AnimationStartTime  = DateTime.UtcNow;
            animState.AttackTriggered     = false;
            animState.CurrentAttackNumber = attackNumber;
            animState.LastAttackTime      = DateTime.UtcNow;

            // 쿨타임 기록
            string attackSerial = $"A{session.SerialNumber}_{attackNumber}";
            session.LastAttackTimes[attackSerial] = DateTime.UtcNow;
            session.AnimationId = 3;

            // 브로드캐스트
            session.Room?.Push(() =>
            {
                var attackAnim = new S_BroadcastEntityAttackAnimation {
                    ID               = session.SessionId,
                    entityType       = session.EntityType,
                    animationID      = 3,
                    attackAnimeNumID = attackNumber,
                    dirX             = 0, 
                    dirY             = 0, 
                    dirZ             = 1
                };
                session.Room.Broadcast(attackAnim.Write());
            });
        }       
         
        /// <summary>
        /// Attack 애니메이션 진행 체크
        /// - AttackInfoData(animeLength, attackTiming) 참조
        /// - 공격 타이밍에 도달하면 데미지 발생
        /// - 애니메이션 완료시 Idle 전환
        /// </summary>
        private void CheckAttackAnimationProgress(CommonSession session, AnimationState animState, DateTime now)
        {
            int attackNumber = animState.CurrentAttackNumber;

            // AttackInfo에서 애니메이션 길이 및 타이밍을 가져온다.
            string attackSerial = $"A{session.SerialNumber}_{attackNumber}"; // 예: AM000_1
            AttackInfoData info = Program.DBManager.GetAttackInfo(attackSerial);

            // 기본값 지정 (정보가 없을 때 대비)
            float attackLength = 1.0f;
            float attackTiming = 0.5f;
            if (info != null)
            {
                float.TryParse(info.animeLength,  out attackLength);
                float.TryParse(info.attackTiming, out attackTiming);
            }
            
            // 시간이 얼마나 지났는지 체크(현재시간 - 시작시간 = 작동시간)
            var elapsed = (now - animState.AnimationStartTime).TotalSeconds;
            
            // 공격 타이밍에 데미지 발생 (한 번만 발생하도록 플래그 사용)
            if (!animState.AttackTriggered && elapsed >= attackTiming && attackTiming > 0)
            {
                RequestAttackDamage(session, attackNumber);
                animState.AttackTriggered = true;
            }
            
            // Attack → Idle 전환 (애니메이션 완료)
            if (elapsed >= attackLength)
            {
                // 플레이어는 클라이언트가 애니메이션을 제어하므로 서버에서 Idle 전환하지 않는다.
                if (session.EntityType == (int)Define.Layer.Player)
                    return;
            
                TransitionToIdle(session, animState);
            }
        }            
        
        /// <summary>
        /// 자동 공격 요청 (GameRoom으로 위임)
        /// </summary>
        private void RequestAttackDamage(CommonSession attacker, int attackNumber)
        {
            //GameRoom에 자동 공격 처리 요청 (기존 AttackCheckToAttackResult 활용)
            attacker.Room?.Push(() => { attacker.Room.ProcessScheduledAttack(attacker, attackNumber); });
        }        
        
        /// <summary>
        /// 공격 쿨다운 판별 - 시리얼넘버 기반 확장 가능한 시스템
        /// - 트랩별, 몬스터별 고유한 공격 주기 설정
        /// </summary>
        private float GetAttackCooldown(string serialNumber, int attackNumber = 1)
        {
            string attackSerial = $"A{serialNumber}_{attackNumber}";
            AttackInfoData info = Program.DBManager.GetAttackInfo(attackSerial);
            if (info == null) return 0f;

            if (float.TryParse(info.coolTime, out float cd))
                return cd;
            return 0f;
        }        
        
        /// <summary>
        /// Idle 상태로 전환하고 브로드캐스트
        /// </summary>
        private void TransitionToIdle(CommonSession session, AnimationState animState)
        {
            // 상태 변경
            animState.CurrentAnimation   = Define.Anime.Idle;
            animState.AnimationStartTime = DateTime.UtcNow;
            animState.MoveDirectionX     = 0;
            animState.MoveDirectionZ     = 0;
            animState.HasMoveDirection   = false;
            
            // 세션 변경
            session.AnimationId = 0;   // Idle 애니메이션 ID
            
            // Idle 상태 브로드캐스트
            session.Room?.Push(() =>
            {
                // Idle 애니메이션 브로드캐스트
                var idleAnimPacket = new S_BroadcastEntityAnimation {
                    ID          = session.SessionId,
                    entityType  = session.EntityType,
                    animationID = 0
                };
                session.Room.Broadcast(idleAnimPacket.Write());
            });
            
            if(session.SerialNumber.StartsWith("M"))
                Console.WriteLine($"{session.SerialNumber}({session.SessionId}) -> Idle 전환");
        }            
        
        // 세션 찾기 (모든 룸에서 검색)
        private CommonSession FindSessionById(int sessionId)
        {
            foreach (var room in Program.GameRooms.Values)
            {
                var session = room._commonSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null) return session;
            }
            return null;
        }              
        
        #endregion
         
        #region Public 메서드들 - 외부에서 호출되는 인터페이스

        /// <summary>
        /// 작업 스케줄링 - 특정 시간에 실행될 작업 등록
        /// - 사망 후 재생성, 버프/디버프 해제 등에 사용
        /// - 멀티스레드 안전성 보장
        /// </summary>
        public void ScheduleTask(DateTime executeTime, Action task, string description, int sessionId = 0, string roomName = "")
        {
            lock (_lock)
            {
                _scheduledTasks.Add(new ScheduledTask
                {
                    ExecuteTime = executeTime,
                    Task        = task,
                    Description = description,
                    SessionId   = sessionId,
                    RoomName    = roomName
                });
            }
        }

        /// <summary>
        /// 애니메이션 상태 설정 - GameRoom에서 호출
        /// - 애니메이션 시작 시점을 기록하여 자동 전환 관리
        /// - 트랩의 경우 반복 공격 설정 자동 적용
        /// </summary>
        public void SetAnimationState(CommonSession session, Define.Anime animationType, int attackNumber = 1)
        {
            lock (_lock)
            {
                if (!_animationStates.ContainsKey(session.SessionId))
                    _animationStates[session.SessionId] = new AnimationState();
                
                var animState = _animationStates[session.SessionId];
                animState.CurrentAnimation    = animationType;
                animState.AnimationStartTime  = DateTime.UtcNow;
                animState.CurrentAttackNumber = attackNumber;
                
                // 자동 공격 엔티티 설정 (트랩)
                // 쿨다운이 있는 오브젝트 자동 공격
                float attackCooldown = GetAttackCooldown(session.SerialNumber, attackNumber);
                if (attackCooldown > 0 && session.EntityType == (int)Define.Layer.Object)
                {
                    animState.IsRepeatingAttack = true;
                    animState.LastAttackTime    = DateTime.UtcNow.AddSeconds(-attackCooldown); // 즉시 첫 공격 가능
                }
            }
        }

        /// <summary>
        /// 애니메이션 상태 제거 - 엔티티 사망/제거 시 호출
        /// - 메모리 누수 방지를 위한 정리 작업
        /// </summary>
        public void RemoveAnimationState(int sessionId)
        {
            lock (_lock)
            {
                _animationStates.Remove(sessionId);
            }
        }
        
        /// <summary>
        /// 히트 시 Run 상태 몬스터의 이동 시간만큼 위치 보정
        /// - 히트 당한 몬스터가 Run 상태였다면 이동한 시간만큼 위치를 먼저 보정
        /// - 이후 StartHitKnockBack 적용으로 정확한 위치 동기화
        /// </summary>
        public void ProcessHitDuringRun(int sessionId)
        {
            lock (_lock)
            {
                if (!_animationStates.TryGetValue(sessionId, out var animState))
                    return;

                // Run 상태가 아니거나 이동 방향이 없으면 처리하지 않음
                if (animState.CurrentAnimation != Define.Anime.Run || !animState.HasMoveDirection)
                    return;

                var session = FindSessionById(sessionId);
                if (session == null || !session.SerialNumber.StartsWith("M"))
                    return;

                // Run 시작부터 현재까지의 시간 계산
                DateTime now = DateTime.UtcNow;
                float elapsedTime = (float)(now - animState.AnimationStartTime).TotalSeconds;
                
                // 이동한 시간만큼 위치 보정
                float moveDistance = session.moveSpeed * elapsedTime;
                session.PosX += animState.MoveDirectionX * moveDistance;
                session.PosZ += animState.MoveDirectionZ * moveDistance;

                Console.WriteLine($"[히트 시 이동 보정] {session.SerialNumber}({session.SessionId}) " +
                                $"이동시간: {elapsedTime:F3}초, 보정위치: ({session.PosX:F2}, {session.PosZ:F2})");

                // 이동 방향 초기화 (히트로 인해 Run 중단)
                animState.MoveDirectionX = 0;
                animState.MoveDirectionZ = 0;
                animState.HasMoveDirection = false;
            }
        }        
        
        #endregion
    }
} 