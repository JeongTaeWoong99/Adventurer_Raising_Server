using System;
using System.Collections.Generic;

// ★ DummyClient 전용
namespace DummyClient
{
	// DummyClient들의 각자의 ServerSession을 통합해서, 관리하는 SessionManager
	// 유니티 실제 클라이언트에서는 필요가 없다.
	// 왜냐하면, 실제 클라에서는 자신꺼 1개의 ServerSession만 있기 때문이다.
	// 지금은 DummyClient들이 많으니까, 한꺼번에 관리할려고, 만든 것
	class DummyClientSessionManager
	{
		static DummyClientSessionManager _dummyClientSession = new DummyClientSessionManager();
		public static DummyClientSessionManager Instance { get { return _dummyClientSession; } }

		List<ServerSession> _sessions = new List<ServerSession>();
		object _lock = new object();
		Random _rand = new Random();
		
		public ServerSession Generate()
		{
			lock (_lock)
			{
				ServerSession session = new ServerSession();
				_sessions.Add(session);
				return session;
			}
		}
		
		public void SendForEach()
		{
			lock (_lock)
			{
				foreach (ServerSession session in _sessions)
				{
					C_Move movePacket = new C_Move();
					movePacket.posX = _rand.Next(-50, 50);
					movePacket.posY = 0;
					movePacket.posZ = _rand.Next(-50, 50);
					
					session.Send(movePacket.Write());
				}
			}
		}

	}
}