using Shared.Networking;
using Shared.Networking.Packets;

namespace GameServer.Tests;

// 맵 이동 거부 패킷의 형식을 고정해두는 테스트. 클라이언트(GameMapChangeRejectedPacket)가 같은 순서로 읽는다.
public class MapChangeRejectedPacketTests
{
    [Theory]
    [InlineData("Floor002", MapChangeRejectReason.Dead)]
    [InlineData("Floor001", MapChangeRejectReason.NotAtPortal)]
    [InlineData("Nowhere", MapChangeRejectReason.UnknownMap)]
    public void RoundTrips(string mapId, MapChangeRejectReason reason)
    {
        var packet = new S2CMapChangeRejected { MapId = mapId, Reason = reason };

        S2CMapChangeRejected decoded = S2CMapChangeRejected.Decode(packet.Encode());

        Assert.Equal(mapId, decoded.MapId);
        Assert.Equal(reason, decoded.Reason);
    }

    [Fact]
    public void Encode_MatchesTheByteLayoutTheClientReads()
    {
        // 문자열은 7비트 길이 접두 + UTF8, 사유는 리틀 엔디언 int32. 클라이언트(GameMapChangeRejectedPacket.Decode)가 같은 순서로 읽는다.
        byte[] encoded = new S2CMapChangeRejected { MapId = "AB", Reason = MapChangeRejectReason.NotAtPortal }.Encode();

        Assert.Equal(new byte[] { 2, (byte)'A', (byte)'B', 2, 0, 0, 0 }, encoded);
    }

    [Fact]
    public void ReasonValuesAreStable()
    {
        // 값이 클라이언트 enum(GameMapChangeRejectedPacket.MapChangeRejectReason)과 정수 그대로 오간다 - 번호를 바꾸면 안 된다.
        Assert.Equal(1, (int)MapChangeRejectReason.Dead);
        Assert.Equal(2, (int)MapChangeRejectReason.NotAtPortal);
        Assert.Equal(3, (int)MapChangeRejectReason.UnknownMap);
    }

    [Fact]
    public void OpCodeValueIsStable()
    {
        Assert.Equal((ushort)0x0228, (ushort)OpCode.Game_MapChangeRejected);
    }
}
