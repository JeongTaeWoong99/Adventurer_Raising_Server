using System;
using System.Collections.Generic;

namespace ServerCore
{
	// IJobQueue 인터페이스(ServerCore의 JobQueue와 Server의 GameRoom가 둘다 들고 있어야 함.)
	public interface IJobQueue
	{
		void Push(Action job);
	}
	
	// 작업을 처리하는 큐를 관리
	// ★ 이 클래스는 단일 스레드에서 작업을 처리하도록 설계됨.
	public class JobQueue : IJobQueue
	{
		object _lock = new object();
		
		Queue<Action> _jobQueue = new Queue<Action>(); // 작업을 저장하는 큐(GameRoom에서 보낸 액션들을 처리 대기하는 큐)
		bool _flush = false;						   // 작업이 비워져 있는지 확인
		
		// 작업을 큐에 추가 및 비우기
		// GameRoom의 작업들이 큐에 들어옴. Enter(),Leave(),Move(),Flush()
		// 작업들을 순차적으로 처리하고, 결과적으로 GameRoom의 _pendingList에 쌓임.
		// 일정한 시간마다, GameRoom의 Flush()가 Push에 들어오면서, 쌓여있던 _pendingList를 클라이언트에 보내고,_pendingList를 비움. 
		public void Push(Action job)
		{
			bool flush = false;
			
			// 큐에 작업을 추가할 때, 큐가 비어있다면 플래그를 설정하여
			// 작업을 처리할 수 있도록 합니다.
			
			// 경우 1 :_flush가 false라면 내가 얘를 이제 실행해야한다는 것을 flush가 true인 것을 통해 알 수 있음
			// push를 할 때 경우에 따라서 맨 처음으로 JobQueue에 일감을 밀어넣었다면 (_flush == false)
			// 실제로 실행까지 담당한다.
			// 경우 2: 그리고 flush를 진행하고 있는 쓰레드가 있다면(flush == true) 일감만 등록하고 빠져나온다.
			// 그리고 JobQueue에 있는 모든 일감을 처리했다면 다음에 Push 하는 쓰레드가 일감을 처리할 수 있도록
			// 다시금 오픈해준다.(_flush == false)
			lock (_lock)
			{
				_jobQueue.Enqueue(job);
				if (_flush == false)
					flush = _flush = true;
			}
			
			// Flush()가 실행 중 이지 않다면, 등록된 작업들 처리.
			if (flush)
				Flush();
		}
		
		// 들어온 Action 처리
		void Flush()
		{
			while (true)
			{
				Action action = Pop();
				if (action == null)
					return;

				action.Invoke(); // 들어온 작업들 처리.
			}
		}

		// 큐에서 작업을 꺼내옴.
		// 큐가 비어있다면 플래그를 해제하고 null을 반환합니다.
		Action Pop()
		{
			lock (_lock)
			{
				if (_jobQueue.Count == 0)
				{
					_flush = false;	// 다시 쌓여있는 작업들을 처리할 수 있게 false 반환.
					return null;
				}
				return _jobQueue.Dequeue();
			}
		}
	}
}