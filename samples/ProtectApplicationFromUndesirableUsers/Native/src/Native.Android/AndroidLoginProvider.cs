﻿using Android.App;
using Android.Content;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx;
using OpenId.AppAuth;
using Org.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Native.Droid
{
    public class AndroidLoginProvider : ILoginProvider
    {
        private readonly AuthorizationService _authService;
        private static AndroidLoginProvider _instance;
        private static AuthState _authState;
        private static AsyncAutoResetEvent _loginResultWaitHandle;

        public AndroidLoginProvider()
        {
            if (_instance == null)
            {
                _instance = this;
            }

            _authService = new AuthorizationService(MainActivity.Instance);
        }

        internal static AndroidLoginProvider Instance
        {
            get
            {
                return _instance;
            }
        }

        public async Task<AuthInfo> LoginAsync()
        {
            _loginResultWaitHandle = new AsyncAutoResetEvent(false);
            try
            {
                using (var httpClient = new HttpClient(GetUnsecuredHandler()))
                {
                    var httpResult = await httpClient.GetAsync("https://10.0.2.2:5001/.well-known/openid-configuration");
                    var json = await httpResult.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(json);
                    var configuration = new AuthorizationServiceConfiguration(
                        Android.Net.Uri.Parse(jObj["authorization_endpoint"].ToString()),
                        Android.Net.Uri.Parse(jObj["token_endpoint"].ToString()))
                    {
                        DiscoveryDoc = new AuthorizationServiceDiscovery(new JSONObject(json))
                    };
                    MakeAuthRequest(configuration, new AuthState());
                    await _loginResultWaitHandle.WaitAsync();
                }
            }
            catch (AuthorizationException) { }


            return new AuthInfo()
            {
                IsAuthorized = _authState?.IsAuthorized ?? false,
                AccessToken = _authState?.AccessToken,
                IdToken = _authState?.IdToken,
                RefreshToken = _authState?.RefreshToken,
                Scope = _authState?.Scope
            };
        }

        private void MakeAuthRequest(AuthorizationServiceConfiguration serviceConfig, AuthState authState)
        {
            var authRequest = new AuthorizationRequest.Builder(
                    serviceConfig, "nativeXamarin",
                    $"{ResponseTypeValues.Code}",
                    Android.Net.Uri.Parse("com.companyname.nativexamarin:/oauth2redirect"))
                .SetScope("openid profile email")
                .Build();
            var postAuthorizationIntent = CreatePostAuthorizationIntent(MainActivity.Instance, authRequest, serviceConfig.DiscoveryDoc, authState);
            _authService.PerformAuthorizationRequest(authRequest, postAuthorizationIntent);
        }

        private PendingIntent CreatePostAuthorizationIntent(Context context, AuthorizationRequest request, AuthorizationServiceDiscovery discoveryDoc, AuthState authState)
        {
            var intent = new Intent(context, typeof(MainActivity));
            intent.PutExtra("authState", authState.JsonSerializeString());

            if (discoveryDoc != null)
            {
                intent.PutExtra(
                    "authServiceDiscovery",
                    discoveryDoc.DocJson.ToString());
            }

            return PendingIntent.GetActivity(context, request.GetHashCode(), intent, 0);
        }

        internal void NotifyOfCallback(Intent intent)
        {
            try
            {
                if (!intent.HasExtra("authState"))
                {
                    _authState = null;
                }
                else
                {
                    try
                    {
                        _authState = AuthState.JsonDeserialize(intent.GetStringExtra("authState"));
                    }
                    catch (JSONException)
                    {
                        _authState = null;
                    }
                }
                if (_authState != null)
                {
                    AuthorizationResponse response = AuthorizationResponse.FromIntent(intent);
                    AuthorizationException authEx = AuthorizationException.FromIntent(intent);
                    _authState.Update(response, authEx);
                    if (response != null)
                    {
                        try
                        {
                            var clientAuthentication = _authState.ClientAuthentication;
                        }
                        catch (ClientAuthenticationUnsupportedAuthenticationMethod)
                        {
                            SetWaitHandle();
                            return;
                        }

                        var request = response.CreateTokenExchangeRequest();
                        using (var httpClient = new HttpClient(GetUnsecuredHandler()))
                        {
                            var jObj = new List<KeyValuePair<string, string>>
                            {
                                new KeyValuePair<string, string>("client_id", request.ClientId),
                                new KeyValuePair<string, string>("code", request.AuthorizationCode),
                                new KeyValuePair<string, string>("code_verifier", request.CodeVerifier),
                                new KeyValuePair<string, string>("grant_type", request.GrantType),
                                new KeyValuePair<string, string>("scope", request.Scope),
                                new KeyValuePair<string, string>("redirect_uri", request.RedirectUri.ToString())
                            };
                            var httpRequest = new HttpRequestMessage
                            {
                                Method = HttpMethod.Post,
                                RequestUri = new Uri(request.Configuration.TokenEndpoint.ToString()),
                                Content = new FormUrlEncodedContent(jObj)
                            };
                            var httpResult = httpClient.SendAsync(httpRequest).Result;
                            var tokenResponseJObject = JObject.Parse(httpResult.Content.ReadAsStringAsync().Result);
                            tokenResponseJObject.Add("request", JObject.Parse(request.JsonSerializeString()));
                            var tokenResponse = TokenResponse.JsonDeserialize(new JSONObject(tokenResponseJObject.ToString()));
                            ReceivedTokenResponse(tokenResponse, null);
                        }
                    }
                }
                else
                {
                    SetWaitHandle();
                }
            }
            catch (Exception)
            {
                SetWaitHandle();
            }
        }

        private void ReceivedTokenResponse(TokenResponse tokenResponse, AuthorizationException authException)
        {
            try
            {
                _authState.Update(tokenResponse, authException);
            }
            catch (Exception) { }
            finally
            {
                SetWaitHandle();
            }
        }

        private HttpClientHandler GetUnsecuredHandler()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                return true;
            };

            return handler;
        }

        private void SetWaitHandle()
        {
            if (_loginResultWaitHandle != null)
            {
                _loginResultWaitHandle.Set();
            }
        }
    }
}