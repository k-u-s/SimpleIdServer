﻿// Copyright (c) SimpleIdServer. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SimpleIdServer.OAuth.Api;
using SimpleIdServer.OAuth.Extensions;
using SimpleIdServer.OpenID.Api.BCAuthorize;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleIdServer.OpenID.Api.BCAuthentication
{
    [Route(SIDOpenIdConstants.EndPoints.BCAuthorize)]
    public class BCAuthorizeController : Controller
    {
        private readonly IBCAuthorizeHandler _bcAuthorizeHandler;

        public BCAuthorizeController(IBCAuthorizeHandler bcAuthorizeHandler)
        {
            _bcAuthorizeHandler = bcAuthorizeHandler;
        }

        [HttpPost]
        public Task<IActionResult> Post([FromBody] JObject jObjBody, CancellationToken cancellationToken)
        {
            var jObjHeader = Request.Headers.ToJObject();
            var context = new HandlerContext(new HandlerContextRequest(Request.GetAbsoluteUriWithVirtualPath(), string.Empty, jObjBody, jObjHeader, null, null));
            return _bcAuthorizeHandler.Handle(context, cancellationToken);
        }
    }
}