using System;
using System.Diagnostics;
using Server;
using ServerCore;
using System.Threading.Tasks;

// 다 조립된 패킷이 무엇을 호출할지 처리하는 클래스.
// PacketHandler에서 패킷이 조립되고, 어떤 작업을 할지를 매번 작성을 해야하긴 함.

// 행위 자체를 Action으로 만들어서 밀어 넣어준다.
// 해야할 일을 JobQueue에 넣어주고 하나씩 뽑아서 처리를 하는 방식으로 변경함.
class PacketHandler
{
	# region ===== DB 영역 =====
	
	public static void C_RequestMakeIdHandler(PacketSession session, IPacket packet) // 로그인 화면 - 룸에 들어가 있지 않음.
	{
		C_RequestMakeId makeIdPtk     = packet  as C_RequestMakeId;
		ClientSession   clientSession = session as ClientSession;
	
		// 아이디 생성 비동기 진행....
		Program.DBManager._auth.MakeIdAsync(makeIdPtk.email, makeIdPtk.password, makeIdPtk.nickName, makeIdPtk.serialNumber,clientSession);
	}
	
	public static void C_RequestLoginHandler(PacketSession session, IPacket packet) // 로그인 화면 - 룸에 들어가 있지 않음.
	{
		C_RequestLogin loginPtk      = packet  as C_RequestLogin;
		ClientSession  clientSession = session as ClientSession;
		
		// 로그인 시도 비동기 진행....
		Program.DBManager._auth.LoginAsync(loginPtk.email, loginPtk.password, clientSession);
		Console.WriteLine("C_RequestLoginHandler");
	}
	
	#endregion
	
	# region ===== 관리 영역 =====
	
	public static async void C_SceneChangeHandler(PacketSession session, IPacket packet) // 룸에 들어가 있지 않음.
	{
		try
		{
			C_SceneChange  sceneChangePaket = packet  as C_SceneChange;
			ClientSession  clientSession    = session as ClientSession;
		
			// mmNumber 기반 씬 변경 시스템
			string mmNumber = sceneChangePaket.mmNumber;
			
			if (string.IsNullOrEmpty(mmNumber))
			{
				Console.WriteLine("mmNumber가 비어있습니다.");
				return;
			}

			// mmNumber로 스폰 정보 가져오기 (O(1) 빠른 검색)
			var (spawnX, spawnY, spawnZ, type, toScene) = Program.DBManager._firestore.GetSpawnPositionByMmNumber(mmNumber);
			
			if (string.IsNullOrEmpty(toScene) || string.IsNullOrEmpty(type))
			{
				Console.WriteLine($"mmNumber '{mmNumber}'에 해당하는 씬 정보를 찾을 수 없습니다.");
				return;
			}

			// Type에 따른 처리 분기
			switch (type)
			{
				case "LoginToSave": // 리얼타임 데이터베이스를 통해, 저장된 savedScene를 받아서, toScene에 넣어주기
					if (clientSession.CurrentHP <= 0 || !clientSession.Live)	// 사망 상태 체크 후 복구
					{
						clientSession.CurrentHP     = clientSession.MaxHP;
						clientSession.Live          = true;
						clientSession.Invincibility = false;
						clientSession.AnimationId   = 0;
						Console.WriteLine($"[LoginToSave] 플레이어 {clientSession.NickName} 사망 상태 복구 완료");
					}
					// 정상적인 LoginToSave 흐름 (사망 상태든 아니든 동일하게 처리)
					toScene = await HandleLoginToSaveMove(clientSession, spawnX, spawnY, spawnZ, toScene);
					break;
					
				case "ForcedMove":  // mmNumber에 해당하는 toScene 사용 (마을로 돌아가기 버튼 또는 일반 워프)
					if (clientSession.CurrentHP <= 0 || !clientSession.Live)	// 사망 상태인 경우에만 상태 복구
					{
						clientSession.CurrentHP     = clientSession.MaxHP;
						clientSession.Live          = true;
						clientSession.Invincibility = false;
						clientSession.AnimationId   = 0;
						Console.WriteLine($"[ForcedMove] 플레이어 {clientSession.NickName} 사망 상태 복구 완료");
					}
					HandleFixedRandomMove(clientSession, spawnX, spawnY, spawnZ, toScene, mmNumber, "ForcedMove");
					break;
					
				case "Portal":		// mmNumber에 해당하는 toScene 사용
					HandleFixedRandomMove(clientSession, spawnX, spawnY, spawnZ, toScene, mmNumber, "Portal");
					break;
					
				default:
					Console.WriteLine($"알 수 없는 Type: {type}");
					return;
			}

			// 대상 룸 찾기 및 입장 처리
			GameRoom targetRoom = null;
			if (Program.GameRooms.TryGetValue(toScene, out targetRoom))
			{
				//Console.WriteLine($"룸 '{targetRoom.SceneName}' 입장 성공.");
			}
			else
			{
				Console.WriteLine($"룸을 찾을 수 없음: {toScene}");
				return;
			}
			
			clientSession.Room = targetRoom; // 세션에 룸 넣어주기
			
			// 룸에 플레이어 추가 및 초기 설정
			targetRoom._commonSessions.Add(clientSession);
			targetRoom.Push(() => targetRoom.NewPlayerSetting(clientSession));
			
			// 리얼타임 데이터베이스에 현재 정보 업데이트
			await UpdateUserDatabase(clientSession, toScene);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
		}
	}
	
	/// LoginToSave: 로그인 시 저장된 씬으로 이동
	/// - 저장된 씬이 없거나 UnKnown이면 Village로 이동
	/// - 저장된 위치가 있으면 해당 위치로
	private static async Task<string> HandleLoginToSaveMove(ClientSession clientSession, float spawnX, float spawnY, float spawnZ, string toScene)
	{
		//Console.WriteLine("LoginToSave 처리 시작");
		
		string userEmail = clientSession.email;
		if (string.IsNullOrEmpty(userEmail))
		{
			Console.WriteLine("세션에 사용자 이메일이 없음.");
			return toScene;
		}

		// 리얼타임 데이터베이스에서 사용자 정보 가져오기
		var userData = await Program.DBManager._realTime.GetUserDataAsync(userEmail);
		if (userData == null)
		{
			Console.WriteLine("사용자 데이터를 찾을 수 없음.");
			return toScene;
		}

		// 저장된 씬이 없거나 UnKnown이면 Village로 이동(처음 생성)
		if (string.IsNullOrEmpty(userData.savedScene) || userData.savedScene == "UnKnown")
		{
			//Console.WriteLine("저장된 씬이 없음 -> Village로 이동");
			clientSession.PosX = 0;
			clientSession.PosY = 0;
			clientSession.PosZ = 0;
			
			// toScene을 Village로 변경
			return "Village";
		}
		// 저장된 위치가 있으면 해당 위치로 이동
		else if (!string.IsNullOrEmpty(userData.savedPosition) && userData.savedPosition != "UnKnown")
		{
			//Console.WriteLine($"저장된 씬으로 이동: {userData.savedScene}");
			
			var savedPosition = Extension.ParseVector3(userData.savedPosition);
			clientSession.PosX = savedPosition.X;
			clientSession.PosY = savedPosition.Y;
			clientSession.PosZ = savedPosition.Z;
			
			// toScene을 저장된 씬으로 변경
			return userData.savedScene;
		}
		
		return toScene;
	}
	
	/// fixedRandomMove: 고정 위치 + 랜덤 오프셋 이동
	/// - forcedMove (UI 버튼 강제이동)와 portal (포탈 이동) 공통 처리
	/// - 지정된 위치에 랜덤성 추가하여 이동
	private static void HandleFixedRandomMove(ClientSession clientSession, float spawnX, float spawnY, float spawnZ, string toScene, string mmNumber, string moveType)
	{
		Console.WriteLine($"{moveType} 이동: mmNumber {mmNumber} -> {toScene}");
		
		// 지정된 위치에 랜덤성 추가
		Random rand = new Random();
		float randomX = (float)(rand.NextDouble() * 2.0 - 1.0); // -1.0 ~ 1.0
		float randomZ = (float)(rand.NextDouble() * 2.0 - 1.0); // -1.0 ~ 1.0
		
		clientSession.PosX = spawnX + randomX;
		clientSession.PosY = spawnY;
		clientSession.PosZ = spawnZ + randomZ;
	}
	
	/// 리얼타임 데이터베이스 업데이트
	private static async Task UpdateUserDatabase(ClientSession clientSession, string toScene)
	{
		string savedPositionString = $"{clientSession.PosX} / {clientSession.PosY} / {clientSession.PosZ}";
		
		// 리얼타임 데이터베이스 업데이트
		_ = Program.DBManager._realTime.UpdateUserSceneAsync   (clientSession.email, toScene);
		_ = Program.DBManager._realTime.UpdateUserPositionAsync(clientSession.email, savedPositionString);
		_ = Program.DBManager._realTime.UpdateUserHpAsync      (clientSession.email, clientSession.CurrentHP.ToString());
		_ = Program.DBManager._realTime.UpdateLevelAsync       (clientSession.email,clientSession.CurrentLevel.ToString());
		_ = Program.DBManager._realTime.UpdateUserExpAsync     (clientSession.email,clientSession.currentExp.ToString());
 	}
	
	public static void C_EntityLeaveHandler(PacketSession session, IPacket packet)
	{
		// 클라이언트 쪽에서 나가고 싶다는 패킷을 명시적으로 보냈을 때, 알아서 나갈 수 있도록 해준다.
		// 클라쪽에서 명시적으로 나가지 않고,강제종료된 경우는
		// ClientSession.cs의 OnDisconnected에서 감지 후, 방에서 나가게 한다.
		C_EntityLeave leave         = packet  as C_EntityLeave;
		ClientSession clientSession = session as ClientSession;
		
		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.EntityLeave(clientSession,leave));
	}
	
	#endregion	
	
	# region ===== 조작 영역 =====
	
	public static void C_EntityMoveHandler(PacketSession session, IPacket packet)
	{
		C_EntityMove movePacket = packet as C_EntityMove;

		if (session is CommonSession gameObjectSession && gameObjectSession.Room != null)
		{
			GameRoom room = gameObjectSession.Room;
			room.Push(() => room.Move(gameObjectSession, movePacket));
		}
	}
	
	public static void C_EntityRotationHandler(PacketSession session, IPacket packet)
	{
		C_EntityRotation rotationPacket = packet as C_EntityRotation;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.Rotation(commonSession, rotationPacket));
		}
	}
	
	public static void C_EntityAnimationHandler(PacketSession session, IPacket packet)
	{
		C_EntityAnimation animePacket = packet as C_EntityAnimation;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.Animation(commonSession, animePacket));
		}
	}
	
	public static void C_EntityAttackAnimationHandler(PacketSession session, IPacket packet)
	{
		C_EntityAttackAnimation attackAnimationPacket = packet as C_EntityAttackAnimation;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.AttackAnimation(commonSession, attackAnimationPacket));
		}
	}
	
	public static void C_EntityDashHandler(PacketSession session, IPacket packet)
	{
		C_EntityDash dashPtk = packet as C_EntityDash;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.Dash(commonSession, dashPtk));
		}
	}
	
	public static void C_EntityAttackCheckHandler(PacketSession session, IPacket packet)
	{
		C_EntityAttackCheck attackAnimationPacket = packet as C_EntityAttackCheck;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.AttackCheckToAttackResult(commonSession, attackAnimationPacket));
		}
	}
	
	// public static void C_EntityAttackHandler(PacketSession session, IPacket packet)
	// {
	// 	C_EntityAttack attack = packet as C_EntityAttack;
	// 	if (session is CommonSession commonSession && commonSession.Room != null)
	// 	{
	// 		GameRoom room = commonSession.Room;
	// 		room.Push(() => room.Attack(commonSession, attack));
	// 	}
	// }
	
	public static void C_ChattingHandler(PacketSession session, IPacket packet)
	{
		C_Chatting chatting = packet as C_Chatting;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.Chatting(commonSession, chatting));
		}
	}
	
	public static void C_EntitySkillCreateHandler(PacketSession session, IPacket packet)
	{
		C_EntitySkillCreate skillCreate = packet as C_EntitySkillCreate;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.SkillCreate(commonSession, skillCreate));
		}
	}
	
	#endregion
}