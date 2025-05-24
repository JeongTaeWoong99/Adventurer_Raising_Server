using System;
using System.Collections.Generic;
using System.Text;

namespace ServerCore
{
	public class PriorityQueue<T> where T : IComparable<T>
	{
		List<T> _heap = new List<T>();
		
		public int Count { get { return _heap.Count; } }
		
		// O(logN)
		// 이진트리 형태로 맨 아래에 추가되고, 아래서부터 위로 올라옴.
		// (Loot자리)0번 자리를 차지하면, 가장 우선순위가 높은(=즉 실행시간이 제일 적은)작업이라는 뜻.
		public void Push(T data)
		{
			// 힙의 맨 끝에 새로운 데이터를 삽입
			_heap.Add(data);
			
			int now = _heap.Count - 1;
			// 도장깨기를 시작(이진트리 형식으로 비교하며, 0번 자리를 향해 나아감)
			while (now > 0)
			{
				// 도장깨기를 시도(중간에서부터 비교)
				// Next의 실행시간 - Now내 실행 시간이 0보다 작으면, 내 남은 작업시간이 더 크다는 소리이니, 부모와 자리 바꾸기 X
				// Next의 실행시간 - Now내 실행 시간이 0보다 크면, 내 남은 작업시간이 더 작다는 소리이니, 부모와 자리 바꾸기 O
				int next = (now - 1) / 2;
				if (_heap[now].CompareTo(_heap[next]) < 0)
					break; // 실패

				// 두 값을 교체한다
				(_heap[now], _heap[next]) = (_heap[next], _heap[now]);

				// 검사 위치를 이동한다
				now = next;
			}
		}
		
		// O(logN)
		public T Pop()
		{
			// 반환할 데이터를 따로 저장(루트자리)
			T ret = _heap[0];
			
			// 마지막 데이터를 루트로 이동한다.(빈 루트 자리를 채워주고, 이동한 값을 제거)
			int lastIndex = _heap.Count - 1;
			_heap[0] = _heap[lastIndex];
			_heap.RemoveAt(lastIndex);
			lastIndex--;

			// 역으로 내려가는 도장깨기 시작
			// 이동한 값의 알맞은 자리를 찾아주는 동시에,
			// 현재 우선순위 큐에서의 (Loot자리)0번 자리가 다시 정해진다.(=> 이동한 값이 오른쪽이든 왼쪽이든 이동하면서, 알맞은 값이 0번 자리로 오기 때문에)
			int now = 0;
			while (true)
			{
				int left  = 2 * now + 1;
				int right = 2 * now + 2;

				int next = now;
				// 왼쪽값이 현재값보다 크면, 왼쪽으로 이동
				if (left <= lastIndex && _heap[next].CompareTo(_heap[left]) < 0)
					next = left;
				// 오른값이 현재값(왼쪽 이동 포함)보다 크면, 오른쪽으로 이동
				if (right <= lastIndex && _heap[next].CompareTo(_heap[right]) < 0)
					next = right;

				// 왼쪽/오른쪽 모두 현재값보다 작으면 종료
				if (next == now)
					break;

				// 두 값을 교체한다
				(_heap[now], _heap[next]) = (_heap[next], _heap[now]);
				// 검사 위치를 이동한다
				now = next;
			}

			return ret;
		}
		
		// 꺼내지 않고, 맨 앞으 값을 확인만 함.
		public T Peek()
		{
			if (_heap.Count == 0)
				return default(T);
			return _heap[0];
		}
	}
}
