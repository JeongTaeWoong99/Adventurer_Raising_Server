using ServerCore;

namespace Server
{
	public abstract class CommonSession : PacketSession
	{
		public abstract int EntityType { get; }	// 자식에서 결정
		
		#region 공통 정보

		public GameRoom Room { get; set; }
		
		public string   serialNumber   { get; set; } 
		public string   nickname       { get; set; } 
		public int      currentHP      { get; set; } 
		public int      currentLevel   { get; set; } 

		public int      SessionId     { get; set; }
		public bool	    Live		  { get; set; } = true;	// 초기 true.
		public bool		Invincibility { get; set; }			// 명시 안해주면, false로 시작. 일부는 따로 true로 설정함.
		public float    PosX          { get; set; } 
		public float    PosY          { get; set; } 
		public float    PosZ          { get; set; }
		public float    RotationY     { get; set; } = 180f; // 초기 정면을 보도록 함.
		public int      AnimationId   { get; set; } = 0;	// 초기 IDLE

		//		"b_Type": "Capsule",
		//		"b_Size": "0.5 / 0.0 / 0.5",

		#endregion

		#region 몬스터 및 오브젝트 정보

		//      "attack": "0",
		//		"moveSpeed": "0",
		// 		"ab_Size": "0.0 / 0.0 / 0.0",
		//      "dropExp": "0",
		//		"find_Radius": "0",
		//		"attack1_Length": "1",
		//		"attack1_Timing": "0.33"

		#endregion
	}
}