using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace GameServer.Networking
{
    // GameServer TCP(SslStream)에서 쓸 서버 인증서를 준비한다.
    // 운영 환경에서는 appsettings.json(GameServer:TlsCertPath/TlsCertPassword)에 실제 인증서(PFX)를 지정해
    // 그 파일을 그대로 쓴다 - 접속 도메인과 일치하는 SAN을 가진 인증서여야 클라이언트가 정상 검증을 통과한다
    // (예: Caddy/certbot으로 발급받은 Let's Encrypt 인증서를 PFX로 변환해 마운트).
    // 지정하지 않으면(로컬 개발 등) 자체 서명 인증서를 최초 1회 생성해 실행 파일 옆에 저장하고, 이후
    // 재시작 시 같은 파일을 재사용한다(매번 새로 만들면 재시작마다 인증서가 바뀐다).
    // 자체 서명 인증서는 Client GameServerConnectManager가 UNITY_EDITOR/DEVELOPMENT_BUILD에서만 신뢰하므로
    // 프로덕션 빌드는 반드시 TlsCertPath에 실제 인증서를 지정해야 접속할 수 있다.
    public static class GameServerCertificateProvider
    {
        private const string SelfSignedCertFileName = "gameserver-dev.pfx";
        private const string SelfSignedCertPassword = "dev-only";

        public static X509Certificate2 Load(IConfiguration configuration)
        {
            string? configuredPath = configuration["GameServer:TlsCertPath"];
            if (!string.IsNullOrEmpty(configuredPath))
            {
                string password = configuration["GameServer:TlsCertPassword"] ?? string.Empty;
                Console.WriteLine($"[GameServer] TLS 인증서 로드 : {configuredPath}");
                return new X509Certificate2(configuredPath, password, X509KeyStorageFlags.Exportable);
            }

            string selfSignedPath = Path.Combine(AppContext.BaseDirectory, SelfSignedCertFileName);
            if (File.Exists(selfSignedPath))
            {
                Console.WriteLine("[GameServer] 기존 자체 서명 인증서를 재사용합니다(개발용).");
                return new X509Certificate2(selfSignedPath, SelfSignedCertPassword, X509KeyStorageFlags.Exportable);
            }

            Console.WriteLine("[GameServer] GameServer:TlsCertPath 설정이 없어 자체 서명 인증서를 새로 생성합니다" +
                "(개발/테스트 전용 - 프로덕션 배포 시에는 반드시 실제 인증서를 지정할 것).");

            byte[] pfxBytes = GenerateSelfSignedPfx();
            File.WriteAllBytes(selfSignedPath, pfxBytes);

            return new X509Certificate2(pfxBytes, SelfSignedCertPassword, X509KeyStorageFlags.Exportable);
        }

        private static byte[] GenerateSelfSignedPfx()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=GameServer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(5));

            return certificate.Export(X509ContentType.Pfx, SelfSignedCertPassword);
        }
    }
}
