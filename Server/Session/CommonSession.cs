using System;
using System.Collections.Generic;
using ServerCore;

namespace Server
{
	public abstract class CommonSession : PacketSession
	{
		public abstract int EntityType { get; }	// 자식에서 결정

		#region 플레이어 전용

		public string email       { get; set; }  // admin123_AT_naver_DOT_com 를 이용하여, 값을 바꾸거나, 받아올 때 사용
		public int    currentExp  { get; set; }
		public int    CurrentGold { get; set; }
		
		#endregion
		
		#region 공통 정보

		public GameRoom Room { get; set; }
		
		public int      SessionId     { get; set; }
		
		public string   SerialNumber   { get; set; } 
		public int      CurrentLevel   { get; set; } 
		public string   NickName       { get; set; } 
		public int      CurrentHP      { get; set; } 
		public int      MaxHP          { get; set; } 
		public bool	    Live		   { get; set; } = true;	// 초기 true.
		public bool		Invincibility  { get; set; }			// 명시 안해주면, false로 시작. 일부는 따로 true로 설정함.
		public bool		BuffInvincibility { get; set; }		// 버프에 의한 무적 상태 (대쉬/idle과 별도 관리)
		public float    PosX           { get; set; } 
		public float    PosY           { get; set; } 
		public float    PosZ           { get; set; }
		public float    RotationY      { get; set; } = 180f;	// 초기 정면을 보도록 함.
		public int      AnimationId    { get; set; } = 0;		// 초기 IDLE
		public float    Body_Size      { get; set; }			// 캐릭터 히트 범위(구 모양으로 체크되기 때문에, 반지름 필요)
		public int      Damage		   { get; set; }

		#endregion

		#region 몬스터 및 오브젝트 정보
		public string MmNumber       { get; set; } // NEW: 관리번호 (O01, M01, ...)
		
		public float  moveSpeed      { get; set; }
		public float  findRadius     { get; set; }
		public float  dropExp        { get; set; }
		public float  hitLength      { get; set; }
		public Dictionary<string, DateTime> LastAttackTimes { get; private set; } = new Dictionary<string, DateTime>(); // 공격별 마지막 사용 시각 (쿨타임 계산용)
		
		#endregion
	}
}