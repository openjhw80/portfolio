using System;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using Firebase.Auth;
using Firebase.Extensions;
using static Seeder.GameRequest;


namespace Seeder
{
    public sealed partial class ServerAgent
    {
        public static string PlatformUID { get { return PlayGamesPlatform.Instance.localUser.id; } }
        public static string PlatformUName { get { return PlayGamesPlatform.Instance.localUser.userName; } }
        public static string ServerUID { get { return FirebaseAuth.DefaultInstance.CurrentUser.UserId; } }

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================         
        private static void InitializeLogin()
        {
            // 구글 플레이 초기화.
            PlayGamesPlatform.DebugLogEnabled = true;
            PlayGamesPlatform.Activate();

            // 파이어베이스 초기화.
            _ = FirebaseAuth.DefaultInstance;
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================    
        // 구글플레이 서비스 로그인
        public static void LoginGPGS(Action<Result<ILocalUser>> cbResult)
        {
            var resultData = new Result<ILocalUser>();
            resultData.Msg = Localization.login_auth_gpgs_start;

            Debug.Log(resultData.Msg);

            PlayGamesPlatform.Instance.Authenticate((status) =>
            {
                // 실패시, 알림 처리.
                if (status != SignInStatus.Success)
                {
                    resultData.ResultCode = RequestResultCode.Login_Auth_PlatformInternal;
                    resultData.Msg = Localization.login_auth_gpgs_err_internal;
                    Debug.Log(resultData.Msg);

                    cbResult?.Invoke(resultData);
                    return;
                }

                var user = PlayGamesPlatform.Instance.localUser;
                Debug.Log($"구글플레이 로그인 성공. Id:{user.id}, Name:{user.userName}, State:{user.state}");

                // 성공시, 알림 처리.
                resultData.ResultCode = RequestResultCode.Sucess;
                resultData.Msg = Localization.login_auth_gpgs_success;
                resultData.Data = user;
                cbResult?.Invoke(resultData);
            });
        }

        // 파이어베이스 서버 로그인
        public static void LoginFireBase(Action<Result<FirebaseUser>> cbResult)
        {
            var resultData = new Result<FirebaseUser>();
            resultData.Msg = Localization.login_auth_server_start;

            Debug.Log(resultData.Msg);

            PlayGamesPlatform.Instance.RequestServerSideAccess(false, (authCode) =>
            {
                if (authCode.IsNullOrEmpty())
                {
                    resultData.ResultCode = RequestResultCode.Login_Auth_AuthCode;
                    resultData.Msg = Localization.login_auth_err_authcode;
                    Debug.Log(resultData.Msg);

                    cbResult?.Invoke(resultData);
                    return;
                }

                // 파이어베이스 인증.
                var credential = PlayGamesAuthProvider.GetCredential(authCode);
                FirebaseAuth.DefaultInstance.SignInAndRetrieveDataWithCredentialAsync(credential).ContinueWithOnMainThread(task =>
                {
                    // 실패시, 알림 처리.
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        resultData.ResultCode = RequestResultCode.Login_Auth_ServerInternal;
                        resultData.Msg = Localization.login_auth_server_err_internal;
                        Debug.Log(resultData.Msg);

                        cbResult?.Invoke(resultData);
                        return;
                    }

                    // 유저 정보 에러.
                    var user = task.Result?.User;
                    if (user == null)
                    {
                        resultData.ResultCode = RequestResultCode.Login_Auth_ServerNoUser;
                        resultData.Msg = Localization.login_auth_server_err_no_user;
                        Debug.Log(resultData.Msg);

                        cbResult?.Invoke(resultData);
                        return;
                    }

                    Debug.Log($"인증된 유저정보. IsAnony:{user.IsAnonymous}, ProviderId:{user.ProviderId}, Name:{user.DisplayName}, Id:{user.UserId}");

                    // 성공시, 알림 처리.
                    resultData.ResultCode = RequestResultCode.Sucess;
                    resultData.Msg = Localization.login_auth_server_success;
                    resultData.Data = user;
                    cbResult?.Invoke(resultData);
                });
            });
        }
    }
}
