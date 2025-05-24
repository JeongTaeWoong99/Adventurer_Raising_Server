using DummyClient;
using ServerCore;

// 다 조립된 패킷이 무엇을 호출할지 처리하는 클래스
// PacketHandler에서 패킷이 조립되고, 어떤 작업을 할지를 매번 작성을 해야하긴 함.
class PacketHandler
{
	public static void S_BroadcastPlayerEnterGameHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastPlayerEnterGame pkt           = packet as S_BroadcastPlayerEnterGame;
		ServerSession              serverSession = session as ServerSession;
		
	}

	public static void S_BroadcastPlayerLeaveGameHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastPlayerLeaveGame pkt           = packet as S_BroadcastPlayerLeaveGame;
		ServerSession              serverSession = session as ServerSession;
	}

	public static void S_BroadcastPlayerListHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastPlayerList  pkt           = packet as S_BroadcastPlayerList;
		ServerSession          serverSession = session as ServerSession;
	}

	public static void S_BroadcastMoveHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastMove pkt           = packet as S_BroadcastMove;
		ServerSession   serverSession = session as ServerSession;
	}
	
	public static void S_BroadcastAnimationHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastMove pkt           = packet as S_BroadcastMove;
		ServerSession   serverSession = session as ServerSession;
	}
	
	public static void S_BroadcastAttackAnimationHandler(PacketSession session, IPacket packet)
	{
		S_BroadcastMove pkt           = packet as S_BroadcastMove;
		ServerSession   serverSession = session as ServerSession;
	}
}
