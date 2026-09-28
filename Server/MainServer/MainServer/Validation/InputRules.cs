using System.Text.RegularExpressions;

namespace MainServer.Validation
{
    // 클라이언트가 보낸 계정/캐릭터 입력값 규칙. 클라이언트 UI도 같은 규칙으로 막지만(UI_CharacterCreatePopup 등) 요청은 얼마든지
    // 직접 만들어 보낼 수 있으므로 서버가 최종 판단한다 - 예전에는 빈 비밀번호, 수백 자짜리 닉네임, 존재하지 않는 외형 번호도
    // 그대로 저장됐다. 규칙을 바꾸면 클라이언트 쪽 제한도 함께 맞춘다.
    // 위반 시 사용자에게 보여줄 메시지를 담아 ArgumentException을 던진다(컨트롤러가 400으로 바꾼다).
    public static partial class InputRules
    {
        // 외형 선택지 개수. 클라이언트 BasicCharacter 프리팹의 CharacterCustomModel(hair/eye/mouthCustomList) 개수와 같아야 한다.
        public const int HairCount = 13;
        public const int EyeCount = 12;
        public const int MouthCount = 12;

        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 64;

        // 아이디: 영문/숫자/밑줄 4~20자.
        [GeneratedRegex("^[a-zA-Z0-9_]{4,20}$")]
        private static partial Regex UsernamePattern();

        // 닉네임(계정/캐릭터 공통): 한글/영문/숫자 2~12자. 클라이언트 UI_CharacterCreatePopup.ValidNicknamePattern과 같다.
        [GeneratedRegex("^[a-zA-Z0-9가-힣]{2,12}$")]
        private static partial Regex NicknamePattern();

        public static void ValidateUsername(string? username)
        {
            if (username is null || !UsernamePattern().IsMatch(username))
                throw new ArgumentException("아이디는 영문, 숫자, 밑줄(_)로 4~20자여야 합니다.");
        }

        public static void ValidatePassword(string? password)
        {
            if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
                throw new ArgumentException($"비밀번호는 {MinPasswordLength}~{MaxPasswordLength}자여야 합니다.");
        }

        // 앞뒤 공백을 뺀 닉네임을 돌려준다(저장도 이 값으로 한다).
        public static string NormalizeNickname(string? nickname)
        {
            string trimmed = nickname?.Trim() ?? string.Empty;
            if (!NicknamePattern().IsMatch(trimmed))
                throw new ArgumentException("닉네임은 한글, 영문, 숫자로 2~12자여야 합니다.");

            return trimmed;
        }

        public static void ValidateCustomization(int hairIndex, int eyeIndex, int mouthIndex)
        {
            if (hairIndex < 0 || hairIndex >= HairCount || eyeIndex < 0 || eyeIndex >= EyeCount || mouthIndex < 0 || mouthIndex >= MouthCount)
                throw new ArgumentException("존재하지 않는 외형입니다.");
        }
    }
}
