using System;
using System.Collections.Generic;

namespace Server
{
	// 서버에서 ClientSession들을 관리하는 매니저	
	class SessionManager
	{
		// 싱글톤
		static SessionManager _session = new SessionManager();
		public static SessionManager Instance { get { return _session; } }
		
		// 티켓 아이디
		object _lock = new object();
		int _sessionId = 0;																 // 고유한 세션 아이디
		Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>(); // 여러개 존재
		
		// 새로 서버에 접속한 클라이언트를 관리해줄, ClientSession(대리자)를 만들어주는 메서드.
		// Session 생성 및 ID 발급
		// _lock통해, 고유한 세션이 만들어짐. =>  ClientSession을 리턴함.
		// _listener.Init(endPoint, () => { return SessionManager.Instance.Generate(); });에서 등록되고, 만들어짐.
		public ClientSession Generate()
		{
			lock (_lock)
			{
				int sessionId = ++_sessionId;

				ClientSession session = new ClientSession();
				session.SessionId = sessionId;
				_sessions.Add(sessionId, session);

				//Console.WriteLine($"서버에 접속 : {sessionId}");
				
				return session;
			}
		}
		
		public ClientSession Find(int id)
		{
			lock (_lock)
			{
				ClientSession session = null;
				_sessions.TryGetValue(id, out session);
				return session;
			}
		}
		
		public void Remove(ClientSession session)
		{
			lock (_lock)
			{
				_sessions.Remove(session.SessionId);
			}
		}
	}
}
