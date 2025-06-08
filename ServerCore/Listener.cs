using System;
using System.Net;
using System.Net.Sockets;

// 서버는 Listener가 필요
namespace ServerCore
{
	public class Listener
	{
		Socket        _listenSocket;
		Func<Session> _sessionFactory;
		
		// sessionFactory : 새로운 클라이언트가 들어오면, 실행할 메서드
		// 여기서는 리스너에 SessionManager.Instance.Generate();를 콜백 함수로 넣어줌.
		public void Init(IPEndPoint endPoint, Func<Session> sessionFactory, int register = 10, int backlog = 100)
		{
			_listenSocket    = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			_sessionFactory += sessionFactory; // 등록

			// 문지기 교육
			_listenSocket.Bind(endPoint);

			// 영업 시작
			// backlog : 최대 대기수
			_listenSocket.Listen(backlog);
			
			// 무한 루프 시작...
			for (int i = 0; i < register; i++)
			{
				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += (OnAcceptCompleted);
				RegisterAccept(args);
			}
		}

		void RegisterAccept(SocketAsyncEventArgs args)
		{
			args.AcceptSocket = null;

			bool pending = _listenSocket.AcceptAsync(args);
			if (pending == false)
				OnAcceptCompleted(null, args);
		}

		void OnAcceptCompleted(object sender, SocketAsyncEventArgs args)
		{
			if (args.SocketError == SocketError.Success)
			{
				// <상속구조>
				// ServerSession/ClientSession <- PacketSession <- Session
				// 등록된 SessionManager.Instance.Generate();를 Invoke로 실행해, 새로운 ClientSession을 만들어줌.
				// 1. 새로운 클라이언트를 위한 Session 객체 생성
				// 서버의 Listener가 클라이언트의 접속을 수락(Accept)하면, 그 클라이언트만을 위한 고유한 Socket 객체가 생성됩니다.
				Session session = _sessionFactory.Invoke();
				
				// 2. 이 클라이언트 전용으로 생성된 Socket(args.AcceptSocket)과 Session 객체를 1:1로 묶어서 통신 시작(=전용 회선 연결)
				session.Start(args.AcceptSocket);
				session.OnConnected(args.AcceptSocket.RemoteEndPoint);
			}
			else
				Console.WriteLine(args.SocketError.ToString());

			RegisterAccept(args);
		}
	}
}