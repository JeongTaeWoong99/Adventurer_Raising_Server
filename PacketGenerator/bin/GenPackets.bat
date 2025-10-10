@echo off
cd /d "%~dp0"
START /WAIT PacketGenerator.exe "..\PDL.xml"

XCOPY /Y "GenPackets.cs"          "..\..\DummyClient\Packet\"
XCOPY /Y "ClientPacketManager.cs" "..\..\DummyClient\Packet\"

XCOPY /Y "GenPackets.cs"          "..\..\Server\Packet\"
XCOPY /Y "ServerPacketManager.cs" "..\..\Server\Packet\"

XCOPY /Y "GenPackets.cs"          "..\..\..\3D_RPG(Git)\Assets\Scripts\Server\Packet\"
XCOPY /Y "ClientPacketManager.cs" "..\..\..\3D_RPG(Git)\Assets\Scripts\Server\Packet\"

echo [INFO] Bat 완료
pause