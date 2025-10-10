using System;
using System.IO;
using System.Xml;

namespace PacketGenerator
{
	// 나머지 부분은 PacketGenerator에서 자동화...
	class Program
	{
		static string genPackets;	  // 실시간으로 parsing 하는 데이터들을 보관
		static ushort packetId = 1000;
		static string packetEnums;

		static string clientRegister; // 실시간으로 parsing 하는 데이터들을 보관
		static string serverRegister; // 실시간으로 parsing 하는 데이터들을 보관

		// ☆ batch파일로 실행해야 함.(자동화)
		static void Main(string[] args)
		{
			string pdlPath = "../PDL.xml";	// '../'는 한칸 뒤 폴더라는 의미이다.

			XmlReaderSettings settings = new XmlReaderSettings()
			{	// 주석 무시			   // 스페이스바 무시
				IgnoreComments = true, IgnoreWhitespace = true
			};
			
			if (args.Length >= 1)
				pdlPath = args[0];
			
			using (XmlReader r = XmlReader.Create(pdlPath, settings))
			{
				// 바로 본문으로 이동
				// <?xml version="1.0" encoding="utf-8" ?> 건너뜀.
				r.MoveToContent();
				
				// xml을 한줄 씩 읽음
				while (r.Read())
				{
					// r.Depth == 1 : 바로 xml 본문으로 이동 => <packet name="PlayerInfoReq">으로 이동
					// r.NodeType == XmlNodeType.Element : packet이 현재 내부 요소 일 때
					// 패킷 깊이 1    && 패킷의 정보가 시작하는 부분
					if (r.Depth == 1 && r.NodeType == XmlNodeType.Element)
						ParsePacket(r);
				}
				
				// 자동 파싱되어 만들어진 패킷들 스크립트 덮어 씌우기
				// GenPackets.cs 덮어 씌우기
				// ★ ParsePacket에서 packetEnums += ... + Environment.NewLine + "\t"; 로 인해
				// 마지막 패킷 뒤에도 불필요한 개행(\r\n)과 탭(\t)이 남아있음.
				// TrimEnd()로 문자열 끝의 불필요한 줄바꿈과 탭 문자를 제거하여 깔끔하게 정리
				packetEnums = packetEnums.TrimEnd('\r', '\n', '\t');
				string fileText = string.Format(PacketFormat.fileFormat, packetEnums, genPackets);
				File.WriteAllText("GenPackets.cs", fileText);
				
				// ClientPacketManager.cs 덮어 씌우기
				string clientManagerText = string.Format(PacketFormat.managerFormat, clientRegister);
				File.WriteAllText("ClientPacketManager.cs", clientManagerText);
				
				// ServerPacketManager.cs 덮어 씌우기 
				string serverManagerText = string.Format(PacketFormat.managerFormat,serverRegister);
				File.WriteAllText("ServerPacketManager.cs", serverManagerText);
				
				Console.WriteLine("PacketGenerator 실행 및 종료");
			}
		}
		
		public static void ParsePacket(XmlReader r)
		{
			// 마지막 부분이면 return
			if (r.NodeType == XmlNodeType.EndElement)
				return;
			
			if (r.Name.ToLower() != "packet")
			{
				Console.WriteLine("Invalid packet node");
				return;
			}	

			string packetName = r["name"];
			if (string.IsNullOrEmpty(packetName))
			{
				Console.WriteLine("Packet without name");
				return;
			}
			
			// GenPackets.cs 만들기(클라 및 서버 공통 생성)
			Tuple<string, string, string> t = ParseMembers(r);
			genPackets  += string.Format(PacketFormat.packetFormat,     packetName, t.Item1, t.Item2, t.Item3);
			packetEnums += string.Format(PacketFormat.packetEnumFormat, packetName, ++packetId) + Environment.NewLine + "\t";
			
			// ClientPacketManager(따로 생성)
			if (packetName.StartsWith("S_") || packetName.StartsWith("s_"))
				clientRegister += string.Format(PacketFormat.managerRegisterFormat, packetName) + Environment.NewLine;
			// ServerPacketManager(따로 생성)
			else
				serverRegister += string.Format(PacketFormat.managerRegisterFormat, packetName) + Environment.NewLine;
		}

		// {1} 멤버 변수들
		// {2} 멤버 변수 Read
		// {3} 멤버 변수 Write
		public static Tuple<string, string, string> ParseMembers(XmlReader r)
		{
			string packetName = r["name"];

			string memberCode = "";
			string readCode   = "";
			string writeCode  = "";
			
			int depth = r.Depth + 1;
			while (r.Read())
			{
				// 현재 depth가 내가 원하는 depth가 아니라면 빠져나가기
				if (r.Depth != depth)
					break;

				string memberName = r["name"];
				if (string.IsNullOrEmpty(memberName))
				{
					Console.WriteLine("Member without name");
					return null;
				}
				
				// memberCode에 이미 내용물이 있다면
				// xml 파싱할 때 한칸 띄어쓰기 해줌
				if (string.IsNullOrEmpty(memberCode) == false)
					memberCode += Environment.NewLine;
				if (string.IsNullOrEmpty(readCode) == false)
					readCode += Environment.NewLine;
				if (string.IsNullOrEmpty(writeCode) == false)
					writeCode += Environment.NewLine;

				string memberType = r.Name.ToLower();
				switch (memberType)
				{
					case "byte":
					case "sbyte":
						memberCode += string.Format(PacketFormat.memberFormat,    memberType, memberName);
						readCode   += string.Format(PacketFormat.readByteFormat,  memberName, memberType);
						writeCode  += string.Format(PacketFormat.writeByteFormat, memberName, memberType);
						break;
					case "bool":
					case "short":
					case "ushort":
					case "int":
					case "long":
					case "float":
					case "double":
						// 고정된 사이트의 타입이라 여기서 한번 끊어줌
						// xml에서 memberFormat, readFormat, writeFormat으로 묶어줄 수 있음
						memberCode += string.Format(PacketFormat.memberFormat, memberType, memberName);
						readCode   += string.Format(PacketFormat.readFormat,   memberName, ToMemberType(memberType), memberType);
						writeCode  += string.Format(PacketFormat.writeFormat,  memberName, memberType);
						break;
					case "string":
						memberCode += string.Format(PacketFormat.memberFormat,      memberType, memberName);
						readCode   += string.Format(PacketFormat.readStringFormat,  memberName);
						writeCode  += string.Format(PacketFormat.writeStringFormat, memberName);
						break;
					case "list":
						Tuple<string, string, string> t = ParseList(r);
						memberCode += t.Item1;
						readCode   += t.Item2;
						writeCode  += t.Item3;
						break;
					default:
						break;
				}
			}
			
			// 한 칸 띄어쓰기가 된 다음에 tap으로 교체
			memberCode = memberCode.Replace("\n", "\n\t");
			readCode   = readCode.Replace("\n", "\n\t\t");
			writeCode  = writeCode.Replace("\n", "\n\t\t");
			return new Tuple<string, string, string>(memberCode, readCode, writeCode);
		}
		
		public static Tuple<string, string, string> ParseList(XmlReader r)
		{
			string listName = r["name"];
			if (string.IsNullOrEmpty(listName))
			{
				Console.WriteLine("List without name");
				return null;
			}

			Tuple<string, string, string> t = ParseMembers(r);

			string memberCode = string.Format(PacketFormat.memberListFormat,
				FirstCharToUpper(listName),
				FirstCharToLower(listName),
				t.Item1,
				t.Item2,
				t.Item3);

			string readCode = string.Format(PacketFormat.readListFormat,
				FirstCharToUpper(listName),
				FirstCharToLower(listName));

			string writeCode = string.Format(PacketFormat.writeListFormat,
				FirstCharToUpper(listName),
				FirstCharToLower(listName));

			return new Tuple<string, string, string>(memberCode, readCode, writeCode);
		}

		public static string ToMemberType(string memberType)
		{
			switch (memberType)
			{
				case "bool":
					return "ToBoolean";
				case "short":
					return "ToInt16";
				case "ushort":
					return "ToUInt16";
				case "int":
					return "ToInt32";
				case "long":
					return "ToInt64";
				case "float":
					return "ToSingle";
				case "double":
					return "ToDouble";
				default:
					return "";
			}
		}

		public static string FirstCharToUpper(string input)
		{
			if (string.IsNullOrEmpty(input))
				return "";
			
			// 첫 번째 문자를 대문자로 바꾼 다음 기존에 있던 소문자 제거
			return input[0].ToString().ToUpper() + input.Substring(1);
		}

		public static string FirstCharToLower(string input)
		{
			if (string.IsNullOrEmpty(input))
				return "";
			
			// 첫 번째 문자를 대문자로 바꾼 다음 기존에 있던 소문자 제거
			return input[0].ToString().ToLower() + input.Substring(1);
		}
	}
}
