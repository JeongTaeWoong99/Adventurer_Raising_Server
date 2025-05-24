using System;
using System.Collections.Generic;
using System.Text;
using ServerCore;

namespace Server
{
	struct JobTimerElem : IComparable<JobTimerElem>
	{
		public int    execTick; // 실행 시간
		public Action action;	// 실행할 작업
		
		// execTick이 작은 순서대로 튀어나와야 한다.
		// 비교를 해야하는 순서가 어떻게 되는지?
		// 순서가 헷갈릴 수도 있는데 추천은 일단 해보고 에러가 뜨면 수정하는 것도 좋은 방법
		public int CompareTo(JobTimerElem other)
		{
			return other.execTick - execTick;
		}
	}

	class JobTimer
	{
		PriorityQueue<JobTimerElem> _pq = new PriorityQueue<JobTimerElem>(); // JobTimerElem 구조체를 리스트로 받는 PriorityQueue 리스트
		object _lock = new object();										 // 작업 넣어줄 때, lock

		public static JobTimer Instance { get; } = new JobTimer();				// 새로운 잡 생성을 위해, get 새로운 JobTimer() 리턴
		
		// 작업 넣어주기...
		// 일정 시간마다, static void FlushRoom()을 넣어서, 무한 루프를 시키기 위해 존재...
		// 바로 실행을 원한다면, tickAfter 디폴드 값으로 사용.
		public void Push(Action action, int tickAfter = 0)
		{
			JobTimerElem job;								  // 새로운 작업 구조체 생성
			job.execTick = Environment.TickCount + tickAfter; // 실행 시간 설정 (현재 시간 + 지연 시간)
			job.action   = action;							  // 실행할 작업 설정

			// 작업 큐에 추가 (스레드 안전하게)
			lock (_lock)
			{
				_pq.Push(job);
			}
		}
		
		// 체크
		public void Flush()
		{
			while (true)
			{
				int now = Environment.TickCount; // 현재 시간 확인
				JobTimerElem job;				 // 새로운 작업 구조체 생성(Invoke()를 위해, 저장용)

				// 작업 큐에서 실행할 작업 확인(스레드 안전성 보장)
				lock (_lock)  
				{
					// 큐가 비어있으면 종료
					if (_pq.Count == 0)
						break;
					
					// 다음 실행할 작업 확인(꺼내지 않고, Peek()으로 확인만 하는 것!)
					job = _pq.Peek();
					
					// 실행 시간이 아직 안 됐으면 종료(다음 실행을 위해, 엿본 작업의 시간 확인)
					if (job.execTick > now)
						break;

					// 실행할 것 이기 때문에 작업을 큐에서 제거(작업은 저장되어서, 제거해도 됨.)
					_pq.Pop();
				}
				
				// 작업 실행
				job.action.Invoke();
			}
		}
	}
}