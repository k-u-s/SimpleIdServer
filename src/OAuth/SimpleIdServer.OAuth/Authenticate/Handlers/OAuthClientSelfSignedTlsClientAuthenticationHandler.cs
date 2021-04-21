﻿// Copyright (c) SimpleIdServer. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SimpleIdServer.OAuth.Domains;
using SimpleIdServer.OAuth.Exceptions;
using SimpleIdServer.OAuth.Extensions;
using SimpleIdServer.OAuth.Infrastructures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleIdServer.OAuth.Authenticate.Handlers
{
    public class OAuthClientSelfSignedTlsClientAuthenticationHandler : IOAuthClientAuthenticationHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OAuthClientSelfSignedTlsClientAuthenticationHandler> _logger;

        public OAuthClientSelfSignedTlsClientAuthenticationHandler(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Indicates that the client authentication to the authorization server will occur using mutual TLS with the client
        /// utilizing a self signed certificate.
        /// </summary>
        public const string AUTH_METHOD = "self_signed_tls_client_auth";
        public string AuthMethod => AUTH_METHOD;

        public async Task<bool> Handle(AuthenticateInstruction authenticateInstruction, OAuthClient client, string expectedIssuer, CancellationToken cancellationToken)
        {
            var certificate = authenticateInstruction.Certificate;
            if (certificate == null)
            {
                throw new OAuthException(ErrorCodes.INVALID_CLIENT_AUTH, ErrorMessages.NO_CLIENT_CERTIFICATE);
            }

            await CheckCertificate(certificate, client);
            return true;
        }

        private async Task CheckCertificate(X509Certificate2 certificate, OAuthClient client)
        {
            if (!certificate.IsSelfSigned())
            {
                _logger.LogError("the certificate is not self signed");
                throw new OAuthException(ErrorCodes.INVALID_CLIENT_AUTH, ErrorMessages.CERTIFICATE_IS_NOT_SELF_SIGNED);
            }

            var jsonWebKeys = await client.ResolveJsonWebKeys(_httpClientFactory);
            foreach(var jsonWebKey in jsonWebKeys)
            {
                var x5c = jsonWebKey.Content.FirstOrDefault(c => c.Key == "x5c");
                if (x5c.Equals(default(KeyValuePair<string, string>)) || string.IsNullOrWhiteSpace(x5c.Value))
                {
                    continue;
                }

                var x5cBase64Str = JArray.Parse(x5c.Value)[0].ToString();
                var clientCertificate = new X509Certificate2(Convert.FromBase64String(x5cBase64Str));
                if(clientCertificate.Thumbprint == certificate.Thumbprint)
                {
                    return;
                }
            }

            throw new OAuthException(ErrorCodes.INVALID_CLIENT_AUTH, ErrorMessages.BAD_SELF_SIGNED_CERTIFICATE);
        }
    }
}