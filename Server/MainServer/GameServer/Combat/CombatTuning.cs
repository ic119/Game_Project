using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GameServer.Combat
{
    // 전투 판정/제한에 쓰는 튜닝 값의 단일 출처. 예전에는 대쉬 무적 시간, 몬스터 선딜, 부활 대기 같은 값이 GameRoom/ClientSession
    // 곳곳에 const로 흩어져 있어, 값 하나를 바꾸려면 코드를 고쳐 다시 빌드해야 했고 클라이언트와의 관계(주석으로만 적힌)도 놓치기 쉬웠다.
    //
    // 기본값은 기존 const와 같다. 바꾸려면 appsettings.json의 "Combat" 섹션(또는 환경변수 Combat__DashInvulnerableSeconds 등)에
    // 같은 이름으로 쓴다. 이름 오타, 범위를 벗어난 값, 서로 맞지 않는 값은 서버 시작 시 바로 거부한다(카탈로그 검증과 같은 방식).
    // 클라이언트와 맞물리는 값(대쉬/부활/콤보 간격)은 Client Utils/CombatTimings.cs의 값과 관계를 지켜야 하고,
    // GameServer.Tests의 CombatTuningTests가 기본값과 그 관계를 검사한다.
    public sealed class CombatTuning
    {
        // 대쉬 시작 후 몬스터 공격에 무적인 시간(초). 클라이언트 대쉬 지속시간(0.25초) + 패킷 지연 여유.
        public double DashInvulnerableSeconds { get; init; } = 0.35;

        // 서버가 인정하는 최소 대쉬 간격(초). 클라이언트 대쉬 주기(지속 0.25 + 쿨다운 1.0)보다 약간 짧게 잡아 시계/지연 오차로 정상 대쉬가 거부되지 않게 한다.
        public double MinDashIntervalSeconds { get; init; } = 1.0;

        // 몬스터 근접 공격 사거리(m)와 공격 간격(초). 간격은 선딜 시작 시점부터 센다.
        public float MonsterMeleeRange { get; init; } = 1.5f;
        public float MonsterAttackIntervalSeconds { get; init; } = 1.5f;

        // 몬스터 공격 선딜(초). 플레이어가 모션을 보고 대쉬로 반응할 시간이다.
        public float MonsterAttackWindupSeconds { get; init; } = 0.4f;

        // 사망 후 자동 부활까지의 시간(초). 클라이언트 부활 팝업 카운트다운(CombatTimings.ReviveDelaySeconds)과 같아야 한다.
        public double ReviveDelaySeconds { get; init; } = 5;

        // 공격 요청 최소 간격(ms)과 최대 사거리(m). 클라이언트 콤보 입력 가드(0.15초)보다 짧아야 정상 콤보 2타가 드롭되지 않는다.
        public double MinAttackIntervalMs { get; init; } = 100;
        public float MaxAttackRange { get; init; } = 5f;

        // 물약 연타로 MainServer 차감 요청이 몰리지 않게 하는 최소 사용 간격(ms).
        public double MinUseItemIntervalMs { get; init; } = 300;

        // 마나 자연 회복: 초당 최대 마나의 몇 %를 회복할지. 0이면 자연 회복을 끈다(체력처럼 레벨업/부활로만 차는 방식).
        // 2.5이면 최대 마나가 가득 차는 데 40초가 걸린다. 근거와 스킬 소모량 가이드는 Server/마나_밸런싱_공식.txt에 있다.
        public double ManaRegenPercentPerSecond { get; init; } = 2.5;

        public float MaxAttackRangeSquared => MaxAttackRange * MaxAttackRange;

        public static CombatTuning Current { get; private set; } = new();

        // 서버 시작 시 한 번 호출한다. 설정이 없으면 기본값을 쓴다. 잘못된 값이면 InvalidOperationException으로 시작을 막는다.
        public static void Configure(IConfiguration configuration)
        {
            Current = Load(configuration.GetSection("Combat"));
        }

        // 테스트에서 값을 직접 넣을 때 쓴다.
        public static void Override(CombatTuning tuning)
        {
            tuning.Validate();
            Current = tuning;
        }

        public static CombatTuning Load(IConfigurationSection section)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                nameof(DashInvulnerableSeconds), nameof(MinDashIntervalSeconds), nameof(MonsterMeleeRange),
                nameof(MonsterAttackIntervalSeconds), nameof(MonsterAttackWindupSeconds), nameof(ReviveDelaySeconds),
                nameof(MinAttackIntervalMs), nameof(MaxAttackRange), nameof(MinUseItemIntervalMs), nameof(ManaRegenPercentPerSecond),
            };

            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (!known.Contains(child.Key))
                {
                    throw new InvalidOperationException($"알 수 없는 전투 설정 키 'Combat:{child.Key}' - 오타가 아닌지 확인하세요. 사용 가능: {string.Join(", ", known.Order())}");
                }
            }

            var defaults = new CombatTuning();
            var tuning = new CombatTuning
            {
                DashInvulnerableSeconds = Read(section, nameof(DashInvulnerableSeconds), defaults.DashInvulnerableSeconds),
                MinDashIntervalSeconds = Read(section, nameof(MinDashIntervalSeconds), defaults.MinDashIntervalSeconds),
                MonsterMeleeRange = (float)Read(section, nameof(MonsterMeleeRange), defaults.MonsterMeleeRange),
                MonsterAttackIntervalSeconds = (float)Read(section, nameof(MonsterAttackIntervalSeconds), defaults.MonsterAttackIntervalSeconds),
                MonsterAttackWindupSeconds = (float)Read(section, nameof(MonsterAttackWindupSeconds), defaults.MonsterAttackWindupSeconds),
                ReviveDelaySeconds = Read(section, nameof(ReviveDelaySeconds), defaults.ReviveDelaySeconds),
                MinAttackIntervalMs = Read(section, nameof(MinAttackIntervalMs), defaults.MinAttackIntervalMs),
                MaxAttackRange = (float)Read(section, nameof(MaxAttackRange), defaults.MaxAttackRange),
                MinUseItemIntervalMs = Read(section, nameof(MinUseItemIntervalMs), defaults.MinUseItemIntervalMs),
                ManaRegenPercentPerSecond = Read(section, nameof(ManaRegenPercentPerSecond), defaults.ManaRegenPercentPerSecond),
            };

            tuning.Validate();
            return tuning;
        }

        // 값 하나하나의 범위와, 서로 어긋나면 게임이 깨지는 관계를 검사한다.
        public void Validate()
        {
            Require(DashInvulnerableSeconds, 0.05, 2, nameof(DashInvulnerableSeconds));
            Require(MinDashIntervalSeconds, 0.1, 10, nameof(MinDashIntervalSeconds));
            Require(MonsterMeleeRange, 0.5, 10, nameof(MonsterMeleeRange));
            Require(MonsterAttackIntervalSeconds, 0.2, 30, nameof(MonsterAttackIntervalSeconds));
            Require(MonsterAttackWindupSeconds, 0.05, 5, nameof(MonsterAttackWindupSeconds));
            Require(ReviveDelaySeconds, 0, 60, nameof(ReviveDelaySeconds));
            Require(MinAttackIntervalMs, 0, 2000, nameof(MinAttackIntervalMs));
            Require(MaxAttackRange, 1, 50, nameof(MaxAttackRange));
            Require(MinUseItemIntervalMs, 0, 10000, nameof(MinUseItemIntervalMs));
            Require(ManaRegenPercentPerSecond, 0, 50, nameof(ManaRegenPercentPerSecond));

            // 대쉬 무적이 최소 대쉬 간격보다 길면 쿨다운 없이 계속 무적이 된다.
            if (DashInvulnerableSeconds >= MinDashIntervalSeconds)
            {
                throw new InvalidOperationException($"Combat:DashInvulnerableSeconds({DashInvulnerableSeconds})는 MinDashIntervalSeconds({MinDashIntervalSeconds})보다 짧아야 합니다 - 아니면 계속 무적이 됩니다.");
            }

            // 선딜이 공격 간격보다 길면 이전 공격의 판정이 끝나기 전에 다음 공격이 시작된다.
            if (MonsterAttackWindupSeconds >= MonsterAttackIntervalSeconds)
            {
                throw new InvalidOperationException($"Combat:MonsterAttackWindupSeconds({MonsterAttackWindupSeconds})는 MonsterAttackIntervalSeconds({MonsterAttackIntervalSeconds})보다 짧아야 합니다.");
            }
        }

        private static double Read(IConfigurationSection section, string key, double fallback)
        {
            string? raw = section[key];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException($"Combat:{key} 값 '{raw}'을(를) 숫자로 읽을 수 없습니다.");
            }

            return value;
        }

        private static void Require(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || value < min || value > max)
            {
                throw new InvalidOperationException($"Combat:{name} 값 {value}이(가) 허용 범위({min} ~ {max})를 벗어났습니다.");
            }
        }
    }
}
