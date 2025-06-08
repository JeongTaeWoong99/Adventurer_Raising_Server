using System;
using System.Numerics;
using Server;
using ServerCore;

// 다 조립된 패킷이 무엇을 호출할지 처리하는 클래스.
// PacketHandler에서 패킷이 조립되고, 어떤 작업을 할지를 매번 작성을 해야하긴 함.

// 행위 자체를 Action으로 만들어서 밀어 넣어준다.
// 해야할 일을 JobQueue에 넣어주고 하나씩 뽑아서 처리를 하는 방식으로 변경함.
class PacketHandler
{
	# region ===== 관리 영역 =====
	// 클라이언트 쪽에서 나가고 싶다는 패킷을 명시적으로 보냈을 때, 알아서 나갈 수 있도록 해준다.
	// 클라쪽에서 명시적으로 나가지 않고,강제종료된 경우는
	// ClientSession.cs의 OnDisconnected에서 감지 후, 방에서 나가게 한다.
	public static void C_MyStateHandler(PacketSession session, IPacket packet)
	{
		C_MyState playerStatePacket = packet  as C_MyState;
		ClientSession clientSession     = session as ClientSession;

		// 패킷에서 룸 이름과 동일한, savedScene 이름을 가져옵니다.
		string   targetSceneName = playerStatePacket.savedScene;
		GameRoom targetRoom      = null;
		
		// 찾는 방 이름이 있음
		if (Program.GameRooms.TryGetValue(targetSceneName, out targetRoom))
			Console.WriteLine("이전 저장된 방 정보가 " + targetRoom.SceneName + ". 방 정상 찾기 성공.");
		// 찾는 방 이름이 없음
		else
		{
			Program.GameRooms.TryGetValue("Village", out targetRoom);
			Console.WriteLine("이전 저장된 방 정보가 Unknown. Village로 이동.");
		}
		clientSession.Room = targetRoom;
		
		// 기본정보 세팅
		clientSession.serialNumber = playerStatePacket.serialNumber;
		clientSession.nickname     = playerStatePacket.nickname;
		clientSession.currentHP    = playerStatePacket.currentHp;
		clientSession.currentLevel = playerStatePacket.currentLevel;
		
		Vector3 savedPos = Extension.ParseVector3(playerStatePacket.savedPos);
		clientSession.PosX = savedPos.X;
		clientSession.PosY = savedPos.Y;
		clientSession.PosZ = savedPos.Z;
		
		// 원래 enter 및 list 작업이 이루어지도록 하기...
		// 해당 룸에서 Enter작업이 실행되기 때문에, 해당 방에 있는 플레이어들에게만 Enter 및 List 작업이 실행됨...
		targetRoom._commonSessions.Add(clientSession);
		targetRoom.Push(() => targetRoom.NewPlayerEnter(clientSession));
	}
	
	public static void C_MyLeaveGameHandler(PacketSession session, IPacket packet)
	{
		ClientSession clientSession = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.EntityLeave(clientSession));
	}
	
	public static void C_EntityInfoChangeHandler(PacketSession session, IPacket packet)
	{
		C_EntityInfoChange  playerInfoChange = packet  as C_EntityInfoChange;
		ClientSession       clientSession    = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.EntityInfoChange(clientSession, playerInfoChange));
	}
	
	public static void C_SceneChangeHandler(PacketSession session, IPacket packet)
	{
		C_SceneChange  sceneChangePaket = packet  as C_SceneChange;
		ClientSession  clientSession    = session as ClientSession;
		
		// 패킷에서 룸 이름과 동일한, savedScene 이름을 가져옵니다.
		string   targetSceneName = sceneChangePaket.toScene;
		GameRoom targetRoom      = null;
			
		// 찾는 방 이름이 있음
		if (Program.GameRooms.TryGetValue(targetSceneName, out targetRoom))
			Console.WriteLine("옮겨 가려는 룸 이름은 " + targetRoom.SceneName + ". 방 정상 찾기 성공.");
		else
		{
			Console.WriteLine("옮겨 가려는 방 찾기 실패.");
			return;
		}
		clientSession.Room = targetRoom;
		
		// 동일하게 옮겨간 방에서 Enter 및 List 진행
		targetRoom._commonSessions.Add(clientSession);
		targetRoom.Push(() => targetRoom.NewPlayerEnter(clientSession));
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
