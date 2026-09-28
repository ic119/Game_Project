using MainServer.AuthServer.Data;
using MainServer.AuthServer.Services;
using MainServer.CharacterServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default 설정이 없습니다.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICharacterService, CharacterService>();

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
if (string.IsNullOrEmpty(builder.Configuration["InternalApi:Key"]))
    throw new InvalidOperationException("InternalApi:Key 설정이 없습니다.");

// 장비 장착 검증에 쓰는 아이템 정의(GameServer와 같은 파일). 없거나 잘못됐으면 첫 장착 요청이 아니라 기동 시점에 실패시킨다.
ItemEquipSlotCatalog.EnsureLoaded(CharacterService.ValidEquipSlots);

var app = builder.Build();

// 컨테이너/클라우드 배포 시 DB에 스키마가 없는 상태로 최초 기동되므로, 시작 시점에 대기 중인 마이그레이션을 적용한다.
using (IServiceScope scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    // 프로덕션(Docker) 환경은 Kestrel이 HTTP만 수신하고, TLS는 앞단 리버스 프록시(Nginx/Caddy)가 담당하는 구성을 전제로 한다.
    // HTTPS 리스너가 없는 상태에서 리다이렉트를 시도하면 경고만 남고 아무 효과가 없으므로 프로덕션에서는 비활성화한다.
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
