
namespace Incheol.Define
{
    public partial class DEFINE 
    {
        public static readonly string LOADING_SCENE = "LoadingScene";

        public static readonly string[] ADDRESSABLE_LABEL = { "default" };

        public static readonly string account_Key = "Player_Account";
        public static readonly string password_Key = "Player_Password";

        /// <summary>
        /// 로그인 정보 저장(계정 기억) 토글의 체크 여부
        /// </summary>
        public static readonly string saveLoginInfoToggle_Key = "Player_SaveLoginInfoToggle";

        /// <summary>
        /// 로그인 정보 저장 토글이 체크된 상태에서 로그인 성공 시 저장되는 계정명(비밀번호는 저장하지 않음)
        /// </summary>
        public static readonly string savedLoginAccount_Key = "Player_SavedLoginAccount";

    }
}