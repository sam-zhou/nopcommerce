﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using DotNetOpenAuth.AspNet;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Services.Authentication.External;

namespace Nop.Plugin.ExternalAuth.WeiXin.Core
{
    public class WeiXinProviderAuthorizer : IWeiXinExternalProviderAuthorizer
    {
#region properties
        private readonly IExternalAuthorizer _authorizer;
        private readonly ExternalAuthenticationSettings _externalAuthenticationSettings;
        private readonly WeiXinExternalAuthSettings _weiXinExternalAuthSettings;
        private readonly HttpContextBase _httpContext;
        private readonly IWebHelper _webHelper;
        private WeiXinClient _weiXinApplication;
#endregion

        public WeiXinProviderAuthorizer(IExternalAuthorizer authorizer,
            ExternalAuthenticationSettings externalAuthenticationSettings,
            WeiXinExternalAuthSettings weiXinExternalAuthSettings,
            HttpContextBase httpContext,
            IWebHelper webHelper)
        {
            _authorizer = authorizer;
            _externalAuthenticationSettings = externalAuthenticationSettings;
            _weiXinExternalAuthSettings = weiXinExternalAuthSettings;
            _httpContext = httpContext;
            _webHelper = webHelper;
        }


        #region Utility


        private Uri GenerateLocalCallbackUri()
        {
            string url = string.Format("{0}plugins/externalauthWeiXin/logincallback", _webHelper.GetStoreLocation());
            return new Uri(url);
        }

        public Uri GenerateServiceLoginUrl()
        {
            var builder = new UriBuilder("https://open.weixin.qq.com/connect/oauth2/authorize");
            var args = new Dictionary<string, string>();
            args.Add("appid", _weiXinExternalAuthSettings.AppId);
            args.Add("redirect_uri", GenerateLocalCallbackUri().AbsoluteUri);
            args.Add("response_type", "code");
            args.Add("scope", "snsapi_base");
            args.Add("state", "STATE#wechat_redirect");
            AppendQueryArgs(builder, args);
            return builder.Uri;
        }

        internal static void AppendQueryArgs(UriBuilder builder, Dictionary<string, string> args)
        {
            if ((args != null) && (args.Any()))
            {
                var builder2 = new StringBuilder(50 + (args.Count() * 10));
                if (!string.IsNullOrEmpty(builder.Query))
                {
                    builder2.Append(builder.Query.Substring(1));
                    builder2.Append('&');
                }
                builder2.Append(CreateQueryString(args));
                builder.Query = builder2.ToString();
            }
        }

        internal static string CreateQueryString(Dictionary<string, string> args)
        {
            if (!args.Any())
            {
                return string.Empty;
            }
            var builder = new StringBuilder(args.Count() * 10);
            foreach (var pair in args)
            {
                builder.Append(pair.Key);
                builder.Append('=');
                if (pair.Key == "redirect_uri")
                {
                    builder.Append(EscapeUriDataStringRfc3986(pair.Value));
                }
                else
                {
                    builder.Append(pair.Value);
                }
                
                builder.Append('&');
            }
            builder.Length--;
            return builder.ToString();
        }

        private static readonly string[] UriRfc3986CharsToEscape = { "!", "*", "'", "(", ")" };

        internal static string EscapeUriDataStringRfc3986(string value)
        {
            var builder = new StringBuilder(Uri.EscapeDataString(value));
            for (int i = 0; i < UriRfc3986CharsToEscape.Length; i++)
            {
                builder.Replace(UriRfc3986CharsToEscape[i], Uri.HexEscape(UriRfc3986CharsToEscape[i][0]));
            }
            return builder.ToString();
        }

        internal static string NormalizeHexEncoding(string url)
        {
            var chars = url.ToCharArray();
            for (int i = 0; i < chars.Length - 2; i++)
            {
                if (chars[i] == '%')
                {
                    chars[i + 1] = char.ToUpperInvariant(chars[i + 1]);
                    chars[i + 2] = char.ToUpperInvariant(chars[i + 2]);
                    i += 2;
                }
            }
            return new string(chars);
        }

        #endregion

        private AuthorizeState RequestAuthentication()
        {
            var authUrl = GenerateServiceLoginUrl().AbsoluteUri;
            return new AuthorizeState("", OpenAuthenticationStatus.RequiresRedirect) { Result = new RedirectResult(authUrl) };
        }

        private WeiXinClient WeiXinApplication
        {
            get { return _weiXinApplication ?? (_weiXinApplication = new WeiXinClient(_weiXinExternalAuthSettings.AppId, _weiXinExternalAuthSettings.AppSecret)); }
        }

        private AuthorizeState VerifyAuthentication(string returnUrl)
        {

            var authResult = WeiXinApplication.VerifyAuthentication(_httpContext, GenerateLocalCallbackUri());

            if (authResult.IsSuccessful)
            {
                if (!authResult.ExtraData.ContainsKey("id"))
                    throw new Exception("Authentication result does not contain id data");

                if (!authResult.ExtraData.ContainsKey("accesstoken"))
                    throw new Exception("Authentication result does not contain accesstoken data");

                var parameters = new OAuthAuthenticationParameters(Provider.SystemName)
                {
                    ExternalIdentifier = authResult.ProviderUserId,
                    OAuthToken = authResult.ExtraData["accesstoken"],
                    OAuthAccessToken = authResult.ProviderUserId,
                };

                if (_externalAuthenticationSettings.AutoRegisterEnabled)
                    ParseClaims(authResult, parameters);

                var result = _authorizer.Authorize(parameters);

                return new AuthorizeState(returnUrl, result);
            }

            var state = new AuthorizeState(returnUrl, OpenAuthenticationStatus.Error);
            var error = authResult.Error != null ? authResult.Error.Message : "Unknown error";
            state.AddError(error);
            return state;
        }

        private void ParseClaims(AuthenticationResult authenticationResult, OAuthAuthenticationParameters parameters)
        {
            var claims = new UserClaims();
            claims.Contact = new ContactClaims();
            if (authenticationResult.ExtraData.ContainsKey("username"))
            {
                claims.Contact.Email = authenticationResult.ExtraData["username"];
            }
            else
            {
                claims.Contact.Email = (authenticationResult.ExtraData["accesstoken"]);
            }
            claims.Name = new NameClaims();
            if (authenticationResult.ExtraData.ContainsKey("name"))
            {
                var name = authenticationResult.ExtraData["name"];
                if (!String.IsNullOrEmpty(name))
                {
                    var nameSplit = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (nameSplit.Length >= 2)
                    {
                        claims.Name.First = nameSplit[0];
                        claims.Name.Last = nameSplit[1];
                    }
                    else
                    {
                        claims.Name.Last = nameSplit[0];
                    }
                }
            }

            parameters.AddClaim(claims);
        }

        public AuthorizeState Authorize(string returnUrl, bool? verifyResponse = null)
        {
            if (!verifyResponse.HasValue)
                throw new ArgumentException("Facebook plugin cannot automatically determine verifyResponse property");

            if (verifyResponse.Value)
                return VerifyAuthentication(returnUrl);

            return RequestAuthentication();
        }
    }
}
