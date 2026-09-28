using MainServer.AuthServer;
using MainServer.AuthServer.Data;
using MainServer.AuthServer.Services;
using MainServer.CharacterServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default 설정이 없습니다.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICharacterService, CharacterService>();
builder.Services.AddSingleton<LoginAttemptTracker>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key 설정이 없습니다.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// GameServer -> MainServer 서버 간 API(api/internal/...) 보호용 공유 키(InternalApiKeyAttribute 참고).
// 비어 있으면 모든 내부 호출이 401로 막혀 처치 보상이 저장되지 않으므로, 설정 누락을 기동 시점에 드러낸다.
string internalApiKey = builder.Configuration["InternalApi:Key"] is { Length: > 0 } key
    ? key
    : throw new InvalidOperationException("InternalApi:Key 설정이 없습니다.");

// JWT 서명 키(HMAC-SHA256)는 32바이트 이상이어야 한다. 운영 환경에서는 예시 값(CHANGE_ME...)으로 기동하지 않는다 -
// 저장소에 공개된 appsettings.json/.env.example의 값이라, 그대로 쓰면 누구나 아무 계정의 토큰을 만들거나 내부 API를 호출할 수 있다.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key는 32바이트 이상이어야 합니다.");

if (!builder.Environment.IsDevelopment()
    && (jwtKey.Contains("CHANGE_ME", StringComparison.Ordinal) || internalApiKey.Contains("CHANGE_ME", StringComparison.Ordinal)))
    throw new InvalidOperationException("운영 환경에서 예시 비밀키(CHANGE_ME...)를 쓰고 있습니다. JWT_KEY/INTERNAL_API_KEY를 실제 랜덤 값으로 설정하세요.");

// 운영 환경(docker-compose)에서는 Caddy가 앞단에서 받아 넘기므로 연결 주소가 항상 Caddy다. Caddy가 붙여 주는
// X-Forwarded-For로 실제 접속자 IP를 복원해야 아래 IP별 요청 제한이 사용자마다 따로 걸린다(안 그러면 전원이 한 IP로 묶인다).
// 컨테이너 내부망(사설 대역)에서 온 헤더만 믿는다 - MainServer는 외부에 직접 노출되지 않는다(docker-compose.yml 참고).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

// 로그인 없이 부를 수 있는 인증 API(로그인/가입/재발급/로그아웃)는 IP당 분당 20회로 제한한다 - 비밀번호 대입과 계정 대량 생성을
// 늦춘다. PC방처럼 여러 명이 한 IP를 쓰는 경우를 고려해 넉넉히 잡았고, 계정 단위 제한은 LoginAttemptTracker가 따로 한다.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "요청이 너무 많습니다. 잠시 후 다시 시도해 주세요." }, ct);
    };
});

// 장비 장착 검증에 쓰는 아이템 정의(GameServer와 같은 파일). 없거나 잘못됐으면 첫 장착 요청이 아니라 기동 시점에 실패시킨다.
ItemEquipSlotCatalog.EnsureLoaded(CharacterService.ValidEquipSlots);

var app = builder.Build();

// 컨테이너/클라우드 배포 시 DB에 스키마가 없는 상태로 최초 기동되므로, 시작 시점에 대기 중인 마이그레이션을 적용한다.
using (IServiceScope scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// 요청 제한이 실제 접속자 IP를 보도록 가장 먼저 둔다.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    // 프로덕션(Docker) 환경은 Kestrel이 HTTP만 수신하고, TLS는 앞단 리버스 프록시(Nginx/Caddy)가 담당하는 구성을 전제로 한다.
    // HTTPS 리스너가 없는 상태에서 리다이렉트를 시도하면 경고만 남고 아무 효과가 없으므로 프로덕션에서는 비활성화한다.
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
