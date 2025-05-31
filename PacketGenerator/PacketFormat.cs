namespace PacketGenerator
{
	// 패킷에 있어서 공통된 부분은 남기고 바뀌는 부분만 따로 집어줌.
	// 보통 실제 데이터가 들어가는 부분이 다르기 때문에 해당 부분을 집어줌.
	class PacketFormat
	{
		// ClientPacketManager.cs 및 ServerPacketManager.cs
		// {0} 패킷 등록
		public static string managerFormat =
@"using ServerCore;	
using System;
using System.Collections.Generic;
using System.Text;

// ※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※
// ☆ 자동 완성 패킷(PacketFormat에서 주석 추가)
// 구분을 위해서, ClientPacketManager.cs와 ServerPacketManager.cs로 스크립트 이름 다르게 함.
public class PacketManager
{{
	// 싱글톤
	static PacketManager _instance = new PacketManager();
	public static PacketManager Instance {{ get {{ return _instance; }} }}

	PacketManager()
	{{
		Register();
	}}

	// ushort : Protocol ID
    // Action<PacketSessio, ArraySegment<byte>> : PacketSession, ArraySegment를 인자로 받는 특정 행동
    // 패킷을 생성하는 기능을 보관
	Dictionary<ushort, Func<PacketSession, ArraySegment<byte>, IPacket>> _makeFunc = new Dictionary<ushort, Func<PacketSession, ArraySegment<byte>, IPacket>>();
	
	// ushort : Protocol ID
    // Action<PacketSessio, Action<PacketSession, IPacket>> : PacketSession, IPacket을 인자로 받는 특정 행동
	Dictionary<ushort, Action<PacketSession, IPacket>> _handler = new Dictionary<ushort, Action<PacketSession, IPacket>>();
	
	// ClientPacketManager.cs와 ServerPacketManager.cs의 등록해야 하는 펑션이 다름.(각각, 사용하는게 정해져 있음.)
	// ClientPacketManager는 PDL.xml에서 'S_'가 붇은 패킷 펑션+핸들 등록하고 사용.(Server에서 Client로 보내는 패킷)
	// ServerPacketManager은 PDL.xml에서 'C_'가 붇은 패킷 펑션+핸들 등록하고 사용.(Client에서 Server로 보내는 패킷)
	// 'S_'는 서버에서 클라 보내는 패킷 형식이니, ClientPacketManager에 서버 패킷을 처리하는 펑션+핸들을 등록을 해줘야 하는 것.
	// 'C_'는 클라에서 서보 보내는 패킷 형식이니, ServerPacketManager에 클라 패킷을 처리하는 펑션+핸들을 등록을 해줘야 하는 것.
	public void Register()
	{{
		{0}
	}}
	
	// 받은 패킷의 확인
	// 클라에서 서버로 보낸 패킷이면, 서버의 ClientSession에서 처리
	// 서버에서 클라로 보낸 패킷이면, 클라의 ServerSession에서 처리
	public void OnRecvPacket(PacketSession session, ArraySegment<byte> buffer, Action<PacketSession, IPacket> onRecvCallback = null)
	{{
		ushort count = 0;
		
		// 패킷의 전체 크기 읽기(현재 코드에서는 사용되지 않지만, 패킷의 유효성을 검사하는 데 사용될 수 있음.)
		ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
		count += 2;
		
		// 패킷 ID 읽기
		ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
		count += 2;
		
		// 패킷 ID에 해당하는 처리 함수 찾기
		// Register에서 미리 만들어 두기 때문에, TryGetValue에서 id로 빠르게 찾고
		// 찾은 만들어져 있는(=_makeFunc)를 func에다 넣어줌(=out func).
		Func<PacketSession, ArraySegment<byte>, IPacket> func = null;
		if (_makeFunc.TryGetValue(id, out func))
		{{
			// 패킷 생성 및 처리
			IPacket packet = func.Invoke(session, buffer);
			
			// ★ onRecvCallback해아하는 작업이 있음.
			// ★ 즉, 실제 클라이언트(=유니티)에서는 메인쓰레드에서만 유니티 작업이 작동하니,
			// ★ PacketQueue.Instance.Push(p) 콜백을 통해, PacketQueue에 작업을 푸쉬해줌.
			// ★ 그리고, 푸쉬해둔, HandlePacket작업을 NetworkManger에서 AllPop하여 처리.
			if (onRecvCallback != null)
				onRecvCallback.Invoke(session, packet);
			// 바로 처리 가능하면, HandlePacket 바로 실행.(서버 or 더미클라)
			else
				HandlePacket(session, packet);
		}}
	}}
	
	// where T : IPacket, new() => T에 조건을 달아줌, IPacket을 상속받아야하고, new가 가능해야 한다.
    // IPacket을 상속한 Packet을 생성한 후 해당 패킷의 Protocol에 따라 해당하는 작업을 실행한다.
    // PacketHandler에 등록한 인터페이스를 호출
	T MakePacket<T>(PacketSession session, ArraySegment<byte> buffer) where T : IPacket, new()
	{{
		T pkt = new T();
		pkt.Read(buffer);
		return pkt;
	}}
	
	// 클라이언트 => Update 메인 쓰레드에서 확인하고, 처리.
	// 더미클라  => 바로 처리.
	// 서버     => 바로 처리.
	public void HandlePacket(PacketSession session, IPacket packet)
	{{
		Action<PacketSession, IPacket> action = null;
		if (_handler.TryGetValue(packet.Protocol, out action))
			action.Invoke(session, packet);
	}}
}}";

		// {0} 패킷 이름
		public static string managerRegisterFormat =
@"
		_makeFunc.Add((ushort)PacketID.{0}, MakePacket<{0}>);
		 _handler.Add((ushort)PacketID.{0}, PacketHandler.{0}Handler);";

		// {0} 패킷 이름/번호 목록
		// {1} 패킷 목록
		public static string fileFormat =
@"using System;
using System.Collections.Generic;
using ServerCore;
using System.Text;

// ※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※※
// ☆ 자동 완성 패킷(PacketFormat에서 주석 추가)
public enum PacketID
{{	
	// 1000번대 = 플레이어
	// 2000번대 = 오브젝트
	// 3000번대 = 몬스터
	// 4000번대 = 공통
	{0}
}}

public interface IPacket
{{
	ushort Protocol {{ get; }}
	void Read(ArraySegment<byte> segment);
	ArraySegment<byte> Write();
}}

{1}
";

		// {0} 패킷 이름
		// {1} 패킷 번호
		public static string packetEnumFormat =
@"{0} = {1},";


		// {0} 패킷 이름
		// {1} 멤버 변수들
		// {2} 멤버 변수 Read
		// {3} 멤버 변수 Write
		public static string packetFormat =
@"public class {0} : IPacket
{{
	{1}

	public ushort Protocol {{ get {{ return (ushort)PacketID.{0}; }} }}

	public void Read(ArraySegment<byte> segment)
	{{
		ushort count = 0;
		count += sizeof(ushort);
		count += sizeof(ushort);
		{2}
	}}

	public ArraySegment<byte> Write()
	{{
		ArraySegment<byte> segment = SendBufferHelper.Open(4096);
		ushort count = 0;

		count += sizeof(ushort);
		Array.Copy(BitConverter.GetBytes((ushort)PacketID.{0}), 0, segment.Array, segment.Offset + count, sizeof(ushort));
		count += sizeof(ushort);
		{3}

		Array.Copy(BitConverter.GetBytes(count), 0, segment.Array, segment.Offset, sizeof(ushort));

		return SendBufferHelper.Close(count);
	}}
}}
";

		// {0} 변수 형식
		// {1} 변수 이름
		public static string memberFormat =
@"public {0} {1};";

		// {0} 리스트 이름 [대문자]
		// {1} 리스트 이름 [소문자]
		// {2} 멤버 변수들
		// {3} 멤버 변수 Read
		// {4} 멤버 변수 Write
		public static string memberListFormat =
@"public class {0}
{{
	{2}

	public void Read(ArraySegment<byte> segment, ref ushort count)
	{{
		{3}
	}}

	public bool Write(ArraySegment<byte> segment, ref ushort count)
	{{
		bool success = true;
		{4}
		return success;
	}}	
}}
public List<{0}> {1}s = new List<{0}>();";

		// {0} 변수 이름
		// {1} To~ 변수 형식
		// {2} 변수 형식
		public static string readFormat =
@"this.{0} = BitConverter.{1}(segment.Array, segment.Offset + count);
count += sizeof({2});";

		// {0} 변수 이름
		// {1} 변수 형식
		public static string readByteFormat =
@"this.{0} = ({1})segment.Array[segment.Offset + count];
count += sizeof({1});";

		// {0} 변수 이름
		public static string readStringFormat =
@"ushort {0}Len = BitConverter.ToUInt16(segment.Array, segment.Offset + count);
count += sizeof(ushort);
this.{0} = Encoding.Unicode.GetString(segment.Array, segment.Offset + count, {0}Len);
count += {0}Len;";

		// {0} 리스트 이름 [대문자]
		// {1} 리스트 이름 [소문자]
		public static string readListFormat =
@"this.{1}s.Clear();
ushort {1}Len = BitConverter.ToUInt16(segment.Array, segment.Offset + count);
count += sizeof(ushort);
for (int i = 0; i < {1}Len; i++)
{{
	{0} {1} = new {0}();
	{1}.Read(segment, ref count);
	{1}s.Add({1});
}}";

		// {0} 변수 이름
		// {1} 변수 형식
		public static string writeFormat =
@"Array.Copy(BitConverter.GetBytes(this.{0}), 0, segment.Array, segment.Offset + count, sizeof({1}));
count += sizeof({1});";

		// {0} 변수 이름
		// {1} 변수 형식
		public static string writeByteFormat =
@"segment.Array[segment.Offset + count] = (byte)this.{0};
count += sizeof({1});";

		// {0} 변수 이름
		public static string writeStringFormat =
@"ushort {0}Len = (ushort)Encoding.Unicode.GetBytes(this.{0}, 0, this.{0}.Length, segment.Array, segment.Offset + count + sizeof(ushort));
Array.Copy(BitConverter.GetBytes({0}Len), 0, segment.Array, segment.Offset + count, sizeof(ushort));
count += sizeof(ushort);
count += {0}Len;";

		// {0} 리스트 이름 [대문자]
		// {1} 리스트 이름 [소문자]
		public static string writeListFormat =
@"Array.Copy(BitConverter.GetBytes((ushort)this.{1}s.Count), 0, segment.Array, segment.Offset + count, sizeof(ushort));
count += sizeof(ushort);
foreach ({0} {1} in this.{1}s)
	{1}.Write(segment, ref count);";

	}
}
