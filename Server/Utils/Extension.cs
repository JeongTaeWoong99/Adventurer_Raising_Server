using System;
using System.ComponentModel;
using System.Numerics;
using Server;

// 편리하게 사용하기 위한, 메서드들 미리 만들어 두고 사용
public static class Extension
{
	public static Vector3 ParseVector3(string value)
	{
		var tokens = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length != 3) 
			return new Vector3(0, 0, 0);
        
		return new Vector3(float.Parse(tokens[0].Trim()), float.Parse(tokens[1].Trim()), float.Parse(tokens[2].Trim()));
	}

	/// <summary>
	/// createPos 값을 이용하여 캐스터의 위치와 방향을 기준으로 실제 월드 좌표를 계산
	/// Unity 좌표계 기준: Z축=앞방향, X축=오른쪽, Y축 회전=시계방향
	/// </summary>
	/// <param name="casterX">캐스터의 X 좌표</param>
	/// <param name="casterY">캐스터의 Y 좌표</param>
	/// <param name="casterZ">캐스터의 Z 좌표</param>
	/// <param name="casterRotationY">캐스터의 Y축 회전 (도 단위)</param>
	/// <param name="createPosString">createPos 문자열 (예: "0.5/1.0/0.5" = 오른쪽0.5, 위1.0, 앞0.5)</param>
	/// <returns>계산된 월드 좌표</returns>
	public static Vector3 ComputeCreateWorldPos(float casterX, float casterY, float casterZ, float casterRotationY, string createPosString)
	{
		// createPos 파싱: X=좌우, Y=상하, Z=앞뒤
		var relativePos = ParseVector3(createPosString);
		
		// Unity 좌표계에서 Y축 회전을 라디안으로 변환
		double radians = casterRotationY * Math.PI / 180.0;
		
		// Unity 좌표계 회전 변환 (Y축 회전, Z축이 앞방향)
		float cos = (float)Math.Cos(radians);
		float sin = (float)Math.Sin(radians);
		
		float worldX, worldZ;
		float worldY = casterY + relativePos.Y; // Y축은 항상 동일 (상하)
		
		// 통일된 좌표계 적용 (ScheduleManager 각도 계산 수정으로 해결)
		worldX = casterX + (relativePos.X * cos + relativePos.Z * sin);
		worldZ = casterZ + (-relativePos.X * sin + relativePos.Z * cos);
		
		return new Vector3(worldX, worldY, worldZ);
	}
}