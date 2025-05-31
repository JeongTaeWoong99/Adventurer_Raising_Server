using System;
using System.ComponentModel;
using System.Numerics;

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
}