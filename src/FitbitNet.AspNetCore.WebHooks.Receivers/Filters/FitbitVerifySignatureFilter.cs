using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.WebHooks.Filters
{
    /// <summary>
    /// 
    /// </summary>
    public class FitbitVerifySignatureFilter : WebHookVerifySignatureFilter, IAsyncResourceFilter
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="hostingEnvironment"></param>
        /// <param name="loggerFactory"></param>
        public FitbitVerifySignatureFilter(
            IConfiguration configuration,
            IHostingEnvironment hostingEnvironment,
            ILoggerFactory loggerFactory)
            : base(configuration, hostingEnvironment, loggerFactory)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public override string ReceiverName => FitbitConstants.ReceiverName;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            var routeData = context.RouteData;
            var request = context.HttpContext.Request;
            if (routeData.TryGetWebHookReceiverName(out var receiverName) &&
                IsApplicable(receiverName) &&
                HttpMethods.IsPost(request.Method))
            {
                // 1. Confirm a secure connection
                var errorResult = EnsureSecureConnection(ReceiverName, context.HttpContext.Request);
                if (errorResult != null)
                {
                    context.Result = errorResult;
                    return;
                }

                // 2. Get the header value
                var header = GetRequestHeader(request, FitbitConstants.SignatureHeaderName, out errorResult);
                if (errorResult != null)
                {
                    context.Result = errorResult;
                    return;
                }

                // note: no need to urldecode as specified by fitbit in their community forum
                // https://community.fitbit.com/t5/Web-API-Development/Subscription-Notification-signature-validation/m-p/921974/highlight/true#M2930
                var expectedHash = Convert.FromBase64String(header);

                // 3. get the OAuth secret from the configuration
                var secretAsString = GetSecretKey(FitbitConstants.ReceiverName, routeData, FitbitConstants.MinLength, FitbitConstants.MaxLength);

                // "consumer_secret&"
                var secret = Encoding.ASCII.GetBytes($"{secretAsString}&");

                // 4. get the actual hash
                var actualHash = await ComputeRequestBodySha1HashAsync(request, secret);

                // 5. Verify
                if (!SecretEqual(expectedHash, actualHash))
                {
                    // todo: need more logging!
                    // todo: need to log remote ip, incoming signature and income message content
                    context.Result = new NotFoundResult();
                    return;
                }
            }

            await next();
        }

        /// <inheritdoc />
        protected override IConfigurationSection GetSecretKeys(string sectionKey, RouteData _)
        {
            if (sectionKey == null)
            {
                throw new ArgumentNullException(nameof(sectionKey));
            }

            var key = ConfigurationPath.Combine(
                WebHookConstants.ReceiverConfigurationSectionKey,
                sectionKey,
                FitbitConstants.OAuthClientSecretKey);

            return Configuration.GetSection(key);
        }
    }
}