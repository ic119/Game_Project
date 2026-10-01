using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Incheol.Modules.Networking;
using Shared.Networking;

namespace GameServer.Tests;

// 클라이언트(Unity)와 서버(Shared)는 패킷/OpCode를 각자 손으로 갖고 있어서, 한쪽만 고치면 조용히 어긋난다(필드 순서/타입 변경 등).
// 이 테스트는 클라이언트의 실제 패킷 코드(Client/.../Modules/Networking/Game*.cs, 테스트 프로젝트에 링크됨)를 서버 패킷과 직접 맞대어
//  1) OpCode 이름/값이 같은지,
//  2) 모든 서버 패킷에 대응하는 클라이언트 패킷이 있고(반대도 마찬가지),
//  3) 한쪽이 인코딩한 바이트를 다른 쪽이 디코딩했을 때 모든 필드 값이 그대로 나오는지(필드 순서/타입/개수 포함)
// 를 확인한다. 패킷을 추가/변경하고 반대편을 빠뜨리면 이 테스트가 실패한다.
public class PacketParityTests
{
    private const string ServerPacketNamespace = "Shared.Networking.Packets";

    // 이름 규칙(S2CFoo -> GameFooPacket)에서 벗어난 짝만 적는다.
    private static readonly Dictionary<string, string> ClientNameOverrides = new()
    {
        ["S2CEquipmentChangedBroadcast"] = "GameEquipmentChangedPacket",
        ["S2CChestSpawnBroadcast"] = "GameChestSpawnPacket",
        ["S2CChestDespawnBroadcast"] = "GameChestDespawnPacket",
    };

    // 패킷이 아니라 다른 패킷 안에 들어가는 구조체라 서버/클라이언트 짝을 따로 검증하지 않는다(포함하는 패킷으로 검증된다).
    private static readonly HashSet<string> ServerNestedTypes = new() { "PlayerInfo", "MonsterInfo" };

    private static IEnumerable<Type> ServerPacketTypes() =>
        typeof(OpCode).Assembly.GetTypes()
            .Where(t => t.Namespace == ServerPacketNamespace && t.IsClass && !t.IsAbstract && !t.IsNested
                        && !ServerNestedTypes.Contains(t.Name) && (t.Name.StartsWith("S2C") || t.Name.StartsWith("C2S")))
            .OrderBy(t => t.Name);

    private static string ClientNameFor(Type serverType)
    {
        if (ClientNameOverrides.TryGetValue(serverType.Name, out string? name))
        {
            return name;
        }

        return "Game" + serverType.Name.Substring(3) + "Packet";
    }

    private static Type? FindClientType(string name) =>
        typeof(GameOpCode).Assembly.GetTypes().FirstOrDefault(t => t.Namespace == "Incheol.Modules.Networking" && t.Name == name);

    [Fact]
    public void OpCodes_HaveSameNamesAndValues()
    {
        var server = Enum.GetNames<OpCode>().ToDictionary(n => n, n => (ushort)Enum.Parse<OpCode>(n));
        var client = Enum.GetNames<GameOpCode>().ToDictionary(n => n, n => (ushort)Enum.Parse<GameOpCode>(n));

        var onlyServer = server.Keys.Except(client.Keys).ToList();
        var onlyClient = client.Keys.Except(server.Keys).ToList();
        var different = server.Keys.Intersect(client.Keys).Where(k => server[k] != client[k]).Select(k => $"{k}: 서버 0x{server[k]:X4} / 클라 0x{client[k]:X4}").ToList();

        Assert.True(onlyServer.Count == 0 && onlyClient.Count == 0 && different.Count == 0,
            $"서버에만 있음: [{string.Join(", ", onlyServer)}] / 클라이언트에만 있음: [{string.Join(", ", onlyClient)}] / 값 불일치: [{string.Join("; ", different)}]");
    }

    [Fact]
    public void EveryServerPacket_HasClientCounterpart_AndViceVersa()
    {
        var problems = new List<string>();
        var mappedClientNames = new HashSet<string>();

        foreach (Type serverType in ServerPacketTypes())
        {
            string clientName = ClientNameFor(serverType);
            mappedClientNames.Add(clientName);
            if (FindClientType(clientName) == null)
            {
                problems.Add($"서버 {serverType.Name}에 대응하는 클라이언트 {clientName}이 없음");
            }
        }

        IEnumerable<string> clientPackets = typeof(GameOpCode).Assembly.GetTypes()
            .Where(t => t.Namespace == "Incheol.Modules.Networking" && t.IsClass && t.Name.StartsWith("Game") && t.Name.EndsWith("Packet"))
            .Select(t => t.Name);
        foreach (string clientName in clientPackets.Where(n => n != "GameBinaryPacket" && !mappedClientNames.Contains(n)))
        {
            problems.Add($"클라이언트 {clientName}에 대응하는 서버 패킷이 없음");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryPacket_RoundTripsBetweenServerAndClient()
    {
        var problems = new List<string>();
        int checkedCount = 0;

        foreach (Type serverType in ServerPacketTypes())
        {
            Type? clientType = FindClientType(ClientNameFor(serverType));
            if (clientType == null)
            {
                continue; // 위 테스트가 보고한다.
            }

            bool serverToClient = serverType.Name.StartsWith("S2C");
            Type sourceType = serverToClient ? serverType : clientType;
            Type targetType = serverToClient ? clientType : serverType;

            MethodInfo? encode = sourceType.GetMethod("Encode", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            MethodInfo? decode = targetType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Static, new[] { typeof(byte[]) });
            if (encode == null || decode == null)
            {
                problems.Add($"{serverType.Name}: {(encode == null ? sourceType.Name + ".Encode" : targetType.Name + ".Decode")}가 없음");
                continue;
            }

            object source = SampleFactory.Create(sourceType);
            try
            {
                var bytes = (byte[])encode.Invoke(source, null)!;
                object target = decode.Invoke(null, new object[] { bytes })!;

                // 한쪽에만 있는 필드(서버 전용 값 등)는 비교에서 제외하되, 디코드 쪽 필드는 모두 원본에 있어야 한다.
                var diffs = new List<string>();
                ValueComparer.Compare(target, source, "", diffs, decodedIsClient: serverToClient);
                if (diffs.Count > 0)
                {
                    problems.Add($"{serverType.Name} ({(serverToClient ? "서버→클라" : "클라→서버")}): {string.Join(", ", diffs)}");
                }

                // 반대 방향 인코더가 있으면(서버 C2S 중 Encode 보유 등) 다시 인코딩한 바이트가 같은지도 본다 - 꼬리에 남는 필드 검출.
                MethodInfo? reencode = targetType.GetMethod("Encode", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
                if (reencode != null)
                {
                    var again = (byte[])reencode.Invoke(target, null)!;
                    if (!bytes.SequenceEqual(again))
                    {
                        problems.Add($"{serverType.Name}: 디코드 후 재인코딩한 바이트가 원본과 다름(길이 {bytes.Length} vs {again.Length})");
                    }
                }

                checkedCount++;
            }
            catch (Exception exception)
            {
                problems.Add($"{serverType.Name}: {(exception.InnerException ?? exception).GetType().Name} - {(exception.InnerException ?? exception).Message}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedCount >= 30, $"검증된 패킷이 너무 적음({checkedCount}) - 패킷 탐색이 깨졌을 수 있음");
    }

    // ----- 샘플 값 생성: 모든 필드를 서로 구분되는 0이 아닌 값으로 채워, 순서/타입이 어긋나면 값이 달라지게 한다. -----
    private static class SampleFactory
    {
        public static object Create(Type type)
        {
            object instance = Activator.CreateInstance(type)!;
            int counter = 1;
            foreach (MemberInfo member in WritableMembers(type))
            {
                Type memberType = MemberType(member);
                SetMember(member, instance, Make(memberType, ref counter));
            }

            return instance;
        }

        private static object Make(Type type, ref int counter)
        {
            int n = counter++;
            if (type == typeof(long)) return 1_000_000_000_000L + n;
            if (type == typeof(int)) return 1000 + n;
            if (type == typeof(short)) return (short)(100 + n);
            if (type == typeof(ushort)) return (ushort)(100 + n);
            if (type == typeof(byte)) return (byte)(10 + n);
            if (type == typeof(float)) return 1.5f + n;
            if (type == typeof(double)) return 2.25 + n;
            if (type == typeof(bool)) return true;
            if (type == typeof(string)) return $"문자열{n}";
            if (type.IsEnum)
            {
                var values = Enum.GetValues(type).Cast<object>().ToArray();
                return values[Math.Min(1, values.Length - 1)];
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type)!;
                for (int i = 0; i < 2; i++)
                {
                    list.Add(Make(type.GetGenericArguments()[0], ref counter));
                }

                return list;
            }

            if (type.IsArray)
            {
                Type element = type.GetElementType()!;
                Array array = Array.CreateInstance(element, 2);
                for (int i = 0; i < 2; i++)
                {
                    array.SetValue(Make(element, ref counter), i);
                }

                return array;
            }

            if (type.IsClass || type.IsValueType)
            {
                object nested = Activator.CreateInstance(type)!;
                foreach (MemberInfo member in WritableMembers(type))
                {
                    SetMember(member, nested, Make(MemberType(member), ref counter));
                }

                return nested;
            }

            throw new NotSupportedException($"샘플을 만들 수 없는 타입: {type}");
        }
    }

    private static IEnumerable<MemberInfo> WritableMembers(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return field;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                yield return property;
            }
        }
    }

    private static Type MemberType(MemberInfo member) => member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;

    private static void SetMember(MemberInfo member, object target, object? value)
    {
        if (member is FieldInfo f) f.SetValue(target, value); else ((PropertyInfo)member).SetValue(target, value);
    }

    private static object? GetMember(MemberInfo member, object target) => member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)member).GetValue(target);

    // ----- 값 비교: 디코드된 쪽과 원본의 같은 이름 멤버를 재귀적으로 비교한다. -----
    // 서버 전용 필드(PlayerInfo.BaseStr 등 네트워크로 나가지 않는 값)는 서버 쪽에만 있어도 된다. 반대로 클라이언트 쪽에 있는 필드는
    // 반드시 서버에도 같은 이름으로 있어야 한다(decodedIsClient가 true면 디코드 쪽이 클라이언트, false면 원본이 클라이언트).
    private static class ValueComparer
    {
        public static void Compare(object? decoded, object? original, string path, List<string> diffs, bool decodedIsClient)
        {
            if (decoded == null || original == null)
            {
                if (!ReferenceEquals(decoded, original)) diffs.Add($"{path}: null 불일치");
                return;
            }

            Type type = decoded.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                // 열거형은 서버/클라 enum 타입이 달라도 정수 값으로 비교한다.
                object a = type.IsEnum ? Convert.ChangeType(decoded, Enum.GetUnderlyingType(type)) : decoded;
                object b = original.GetType().IsEnum ? Convert.ChangeType(original, Enum.GetUnderlyingType(original.GetType())) : original;
                if (!Equals(a, b)) diffs.Add($"{path}: {b} → {a}");
                return;
            }

            if (decoded is IEnumerable decodedSeq && original is IEnumerable originalSeq)
            {
                var d = decodedSeq.Cast<object?>().ToList();
                var o = originalSeq.Cast<object?>().ToList();
                if (d.Count != o.Count)
                {
                    diffs.Add($"{path}: 개수 {o.Count} → {d.Count}");
                    return;
                }

                for (int i = 0; i < d.Count; i++)
                {
                    Compare(d[i], o[i], $"{path}[{i}]", diffs, decodedIsClient);
                }

                return;
            }

            // 서버는 (ItemId, Qty) 같은 튜플을, 클라이언트는 이름 있는 클래스를 쓴다 - 튜플은 선언 순서(위치)로 맞춘다.
            if (decoded is ITuple || original is ITuple)
            {
                List<object?> d = Positional(decoded);
                List<object?> o = Positional(original);
                if (d.Count != o.Count)
                {
                    diffs.Add($"{path}: 항목 수 {o.Count} → {d.Count}");
                    return;
                }

                for (int i = 0; i < d.Count; i++)
                {
                    Compare(d[i], o[i], $"{path}.#{i + 1}", diffs, decodedIsClient);
                }

                return;
            }

            Type originalType = original.GetType();
            var decodedNames = new HashSet<string>(WritableMembers(type).Select(m => m.Name));
            foreach (MemberInfo member in WritableMembers(type))
            {
                MemberInfo? other = FindMember(originalType, member.Name);
                string memberPath = path.Length == 0 ? member.Name : $"{path}.{member.Name}";
                if (other == null)
                {
                    if (decodedIsClient) diffs.Add($"{memberPath}: 서버에 같은 이름의 필드가 없음");
                    continue;
                }

                Compare(GetMember(member, decoded), GetMember(other, original), memberPath, diffs, decodedIsClient);
            }

            // 원본이 클라이언트(C2S)일 때는 디코드된 서버 쪽에 없는 클라이언트 필드도 잡아낸다.
            if (!decodedIsClient)
            {
                foreach (MemberInfo member in WritableMembers(originalType).Where(m => !decodedNames.Contains(m.Name)))
                {
                    diffs.Add($"{(path.Length == 0 ? member.Name : $"{path}.{member.Name}")}: 서버에 같은 이름의 필드가 없음");
                }
            }
        }

        private static MemberInfo? FindMember(Type type, string name) =>
            (MemberInfo?)type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        // 튜플이면 Item1..N 순서로, 그 외 객체는 쓰기 가능한 멤버를 선언 순서(MetadataToken)대로 값 목록으로 만든다.
        private static List<object?> Positional(object value)
        {
            if (value is ITuple tuple)
            {
                var items = new List<object?>();
                for (int i = 0; i < tuple.Length; i++) items.Add(tuple[i]);
                return items;
            }

            return WritableMembers(value.GetType()).OrderBy(m => m.MetadataToken).Select(m => GetMember(m, value)).ToList();
        }
    }
}
