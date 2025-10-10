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
		
		// 잠금
		object _lock = new object();
		
		// 티켓 아이디
		int _sessionId = 0;																 // 고유한 세션 아이디
		Dictionary<int, CommonSession> _sessions = new Dictionary<int, CommonSession>(); // 모든 세션(클라/오브젝트/몬스터)
		
		// 연결 통계
		DateTime _lastConnectionTime = DateTime.MinValue;
		int _recentConnectionCount = 0;
		const int MAX_CONNECTIONS_PER_SECOND = 10; // 초당 최대 연결 수
		
		// 새로 서버에 접속한 클라이언트를 관리해줄, ClientSession(대리자)를 만들어주는 메서드.
		// Session 생성 및 ID 발급
		// _lock통해, 고유한 세션이 만들어짐. =>  ClientSession을 리턴함.
		// _listener.Init(endPoint, () => { return SessionManager.Instance.Generate(); });에서 등록되고, 만들어짐.
		public ClientSession ClientSessionGenerate()
		{
			lock (_lock)
			{
				int sessionId = ++_sessionId;
				
				ClientSession session = new ClientSession();
				session.SessionId = sessionId;
				_sessions.Add(sessionId, session);
				
				return session;
			}
		}
		
		// 오브젝트 세션 생성
		public ObjectSession ObjectSessionGenerate()
		{
			lock (_lock)
			{
				int sessionId = ++_sessionId;

				ObjectSession session = new ObjectSession();
				session.SessionId = sessionId;
				_sessions.Add(sessionId, session);
				
				return session;
			}
		}
		
		// 몬스터 세션 생성
		public MonsterSession MonsterSessionGenerate()
		{
			lock (_lock)
			{
				int sessionId = ++_sessionId;

				MonsterSession session = new MonsterSession();
				session.SessionId = sessionId;
				_sessions.Add(sessionId, session);
				
				return session;
			}
		}
		
		// 찾기
		public CommonSession Find(int id)
		{
			lock (_lock)
			{
				CommonSession session = null;
				_sessions.TryGetValue(id, out session);
				return session;
			}
		}
		
		// 제거
		public void Remove(CommonSession session)
		{
			lock (_lock)
			{
				_sessions.Remove(session.SessionId);
			}
		}
	}
}
