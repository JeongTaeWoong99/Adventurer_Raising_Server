using Server;
using ServerCore;

// 다 조립된 패킷이 무엇을 호출할지 처리하는 클래스.
// PacketHandler에서 패킷이 조립되고, 어떤 작업을 할지를 매번 작성을 해야하긴 함.

// 행위 자체를 Action으로 만들어서 밀어 넣어준다.
// 해야할 일을 JobQueue에 넣어주고 하나씩 뽑아서 처리를 하는 방식으로 변경함.
class PacketHandler
{
	// 클라이언트 쪽에서 나가고 싶다는 패킷을 명시적으로 보냈을 때, 알아서 나갈 수 있도록 해준다.
	// 클라쪽에서 명시적으로 나가지 않고,강제종료된 경우는
	// ClientSession.cs의 OnDisconnected에서 감지 후, 방에서 나가게 한다.
	public static void C_PlayerLeaveGameHandler(PacketSession session, IPacket packet)
	{
		ClientSession clientSession = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.Leave(clientSession));
	}

	public static void C_MoveHandler(PacketSession session, IPacket packet)
	{
		C_Move        movePacket    = packet  as C_Move;
		ClientSession clientSession = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.Move(clientSession, movePacket));
	}
	
	public static void C_AnimationHandler(PacketSession session, IPacket packet)
	{
		C_Animation   animePacket   = packet  as C_Animation;
		ClientSession clientSession = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.Animation(clientSession, animePacket));
	}
	
	public static void C_AttackAnimationHandler(PacketSession session, IPacket packet)
	{
		C_AttackAnimation attackAnimationPacket = packet  as C_AttackAnimation;
		ClientSession     clientSession         = session as ClientSession;

		if (clientSession.Room == null)
			return;
		
		GameRoom room = clientSession.Room;
		room.Push(() => room.AttackAnimation(clientSession, attackAnimationPacket));
	}
}
