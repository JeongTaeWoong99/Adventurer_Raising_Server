using ServerCore;

namespace Server
{
	public abstract class CommonSession : PacketSession
	{
		public abstract int EntityType { get; }	// 자식에서 결정

		#region 플레이어 전용

		public string email { get; set; }  // admin123_AT_naver_DOT_com 를 이용하여, 값을 바꾸거나, 받아올 때 사용
		public int    CurrentExp  { get; set; } 
		public int    CurrentGold { get; set; }
		
		#endregion
		
		#region 공통 정보

		public GameRoom Room { get; set; }
		
		public int      SessionId     { get; set; }
		
		public string   SerialNumber   { get; set; } 
		public int      CurrentLevel   { get; set; } 
		public string   NickName       { get; set; } 
		public int      CurrentHP      { get; set; } 
		public int      MaxHP      { get; set; } 
		public bool	    Live		  { get; set; } = true;	// 초기 true.
		public bool		Invincibility { get; set; }			// 명시 안해주면, false로 시작. 일부는 따로 true로 설정함.
		public float    PosX          { get; set; } 
		public float    PosY          { get; set; } 
		public float    PosZ          { get; set; }
		public float    RotationY     { get; set; } = 180f; // 초기 정면을 보도록 함.
		public int      AnimationId   { get; set; } = 0;	// 초기 IDLE
		public float    Body_Size     { get; set; }			// 캐릭터 히트 범위(구 모양으로 체크되기 때문에, 반지름 필요)
		public int      Damage		  { get; set; }

		#endregion

		#region 몬스터 및 오브젝트 정보
		
		public float moveSpeed   { get; set; }
		public float findRadius  { get; set; }
		public float dropExp     { get; set; }
		//		"attack1_Length": "1",
		//		"attack1_Timing": "0.33"
		
		#endregion
	}
}