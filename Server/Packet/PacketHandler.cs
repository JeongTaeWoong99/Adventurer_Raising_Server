using System;
using System.Numerics;
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
	}
	
	#endregion
	
	# region ===== 관리 영역 =====
	
	public static async void C_SceneChangeHandler(PacketSession session, IPacket packet) // 룸에 들어가 있지 않음.
	{
		C_SceneChange  sceneChangePaket = packet  as C_SceneChange;
		ClientSession  clientSession    = session as ClientSession;
		
		// 씬 변경 시 스폰 위치 설정
		string fromScene = sceneChangePaket.fromScene;
		string toScene   = sceneChangePaket.toScene;

		// 로그인 화면에서, 씬 이동(저장된 정보를 불러와서, 해당 위치로 이동)
		if (fromScene == "Login")
		{
			Console.WriteLine("로그인 이동!");
			
			// 세션에서 사용자 이메일 가져오기 (= 키값)
			string userEmail = clientSession.email;
			
			if (!string.IsNullOrEmpty(userEmail))
			{
				// 리얼타임 데이터베이스에서 사용자 정보 가져오기(비동기 작업...)
				var userData = await Program.DBManager._realTime.GetUserDataAsync(userEmail);
				
				if (userData != null)
				{
					// 저장되어 있는 씬의 정보가 UnKnown이면, Village로 이동
					if (string.IsNullOrEmpty(userData.savedScene) || userData.savedScene == "UnKnown")
					{
						Console.WriteLine("저장된 씬이 UnKnown -> Village로 이동");
						
						// Village의 기본 스폰 위치로 이동 ("UnKnown_Village" 키 사용)
						var villageSpawnPosition = Program.DBManager._firestore.GetSpawnPosition("UnKnown", "Village");
						clientSession.PosX = villageSpawnPosition.x;
						clientSession.PosY = villageSpawnPosition.y;
						clientSession.PosZ = villageSpawnPosition.z;
						
						// toScene을 Village로 변경 (실제 이동할 씬)
						toScene = "Village";
					}
					else
					{
						Console.WriteLine($"저장된 씬으로 이동: {userData.savedScene}");
						
						// 저장되어 있는 씬의 정보가 있다면, savedPosition을 가져와서, 해당 위치로 이동
						if (!string.IsNullOrEmpty(userData.savedPosition) && userData.savedPosition != "UnKnown")
						{
							var savedPosition = Extension.ParseVector3(userData.savedPosition);
							clientSession.PosX = savedPosition.X;
							clientSession.PosY = savedPosition.Y;
							clientSession.PosZ = savedPosition.Z;
							
							Console.WriteLine($"저장된 위치로 이동: ({savedPosition.X}, {savedPosition.Y}, {savedPosition.Z})");
						}
						else
						{
							// savedPosition이 없으면 해당 씬의 기본 위치로 이동
							clientSession.PosX = 0;
							clientSession.PosY = 0;
							clientSession.PosZ = 0;
							Console.WriteLine("기본 스폰 위치로 이동: (0, 0, 0");
						}
						
						// toScene을 저장된 씬으로 변경
						toScene = userData.savedScene;
					}
				}
				else
				{
					Console.WriteLine("사용자 데이터를 찾을 수 없음.");
					return;
				}
			}
			else
			{
				Console.WriteLine("세션에 사용자 이메일이 없음.");
				return;
			}
		}
		// 포탈 이동(넘어가는 씬의 알맞을 포탈 위치로 이동)
		else
		{
			Console.WriteLine("포탈 이동!");
			// FirestoreManager에서 스폰 위치 가져오기
			var spawnPosition = Program.DBManager._firestore.GetSpawnPosition(fromScene, toScene);
			
			// 세션 위치 업데이트
			clientSession.PosX = spawnPosition.x;
			clientSession.PosY = spawnPosition.y;
			clientSession.PosZ = spawnPosition.z;
		}
		
		// 패킷에서 룸 이름과 동일한, savedScene 이름을 가져옵니다.
		string   targetSceneName = toScene;  // toScene으로 변경됨
		GameRoom targetRoom      = null;
			
		// 찾는 방 이름이 있음
		if (Program.GameRooms.TryGetValue(targetSceneName, out targetRoom))
			Console.WriteLine("옮겨 가려는 룸 이름은 " + targetRoom.SceneName + ". 방 정상 찾기 성공.");
		else
		{
			Console.WriteLine("옮겨 가려는 방 찾기 실패.");
			return;
		}
		clientSession.Room = targetRoom; // 세션에 룸 넣어주기
		
		// 옮겨간 방에서 Enter 및 List 진행
		targetRoom._commonSessions.Add(clientSession);
		targetRoom.Push(() => targetRoom.NewPlayerSetting(clientSession));
		
		// 씬 변경 완료 후 리얼타임 데이터베이스에 현재 정보 업데이트
		string savedPositionString = $"{clientSession.PosX} / {clientSession.PosY} / {clientSession.PosZ}";
		
		// 리얼타임 데이터베이스에 savedScene 업데이트
		bool sceneUpdateResult = await Program.DBManager._realTime.UpdateUserSceneAsync(clientSession.email, toScene);
		
		// 리얼타임 데이터베이스에 savedPosition 업데이트  
		bool positionUpdateResult = await Program.DBManager._realTime.UpdateUserPositionAsync(clientSession.email, savedPositionString);
		
		if (sceneUpdateResult && positionUpdateResult) Console.WriteLine($"사용자 위치 정보 업데이트 성공: {clientSession.email} -> {toScene} ({savedPositionString})");
		else										   Console.WriteLine($"사용자 위치 정보 업데이트 실패: {clientSession.email}");
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
	
	public static void C_EntityAttackCheckHandler(PacketSession session, IPacket packet)
	{
		C_EntityAttackCheck attackAnimationPacket = packet as C_EntityAttackCheck;
		if (session is CommonSession commonSession && commonSession.Room != null)
		{
			GameRoom room = commonSession.Room;
			room.Push(() => room.AttackCheckToAttackResult(commonSession, attackAnimationPacket));
		}
	}
	#endregion
}
