namespace Incheol.Utils
{
    /// <summary>
    /// TMP_InputField.onValidateInput에 그대로 꽂아 쓰는 문자 검증 델리게이트 모음.
    /// </summary>
    public static class InputFieldValidator
    {
        /// <summary>
        /// 영문(a-z, A-Z)과 숫자(0-9)만 허용한다. 한글(IME 조합 포함)/특수문자/공백은 전부 차단한다.
        /// 계정 아이디처럼 서버 규칙과 무관하게 클라이언트에서 입력 자체를 막아야 하는 필드에 사용.
        /// </summary>
        public static char AllowEnglishAndDigitsOnly(string _text, int _charIndex, char _addedChar)
        {
            bool isAllowed = (_addedChar >= 'a' && _addedChar <= 'z')
                || (_addedChar >= 'A' && _addedChar <= 'Z')
                || (_addedChar >= '0' && _addedChar <= '9');

            return isAllowed ? _addedChar : '\0';
        }
    }
}
