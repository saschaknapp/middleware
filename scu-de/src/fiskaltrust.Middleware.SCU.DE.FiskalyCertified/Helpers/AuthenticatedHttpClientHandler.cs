﻿using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using fiskaltrust.Middleware.SCU.DE.FiskalyCertified.Exceptions;
using fiskaltrust.Middleware.SCU.DE.FiskalyCertified.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace fiskaltrust.Middleware.SCU.DE.FiskalyCertified.Helpers
{
    public class AuthenticatedHttpClientHandler : HttpClientHandler
    {
        private const string ENDPOINT = "auth";

        private readonly FiskalySCUConfiguration _config;
        private string _accessToken;
        private DateTime? _expiresOn;
        private readonly ILogger _logger;

        public AuthenticatedHttpClientHandler(FiskalySCUConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        internal async Task<string> GetToken()
        {
            if (!IsTokenExpired())
            {
                return _accessToken;
            }

            var url = _config.ApiEndpoint.EndsWith("/") ? _config.ApiEndpoint : $"{_config.ApiEndpoint}/";
            using var client = new HttpClient(new HttpClientHandler { Proxy = ConfigurationHelper.CreateProxy(_config) }, disposeHandler: true)
            {
                BaseAddress = new Uri(url),
                Timeout = TimeSpan.FromMilliseconds(_config.FiskalyClientTimeout)
            };

            var requestObject = new TokenRequestDto
            {
                ApiKey = _config.ApiKey,
                ApiSecret = _config.ApiSecret
            };

            var requestContent = JsonConvert.SerializeObject(requestObject);

            var responseMessage = await client.PostAsync(ENDPOINT, new StringContent(requestContent, Encoding.UTF8, "application/json"));

            if (!responseMessage.IsSuccessStatusCode)
            {
                var content = await responseMessage.Content.ReadAsStringAsync();
                throw new FiskalyException($"Could not get OAuth token from Fiskaly API (Status code: {responseMessage.StatusCode}, Response: {content})");
            }

            var responseContent = await responseMessage.Content.ReadAsStringAsync();
            var response = JsonConvert.DeserializeObject<TokenResponseDto>(responseContent);
            _accessToken = response.AccessToken;
            _expiresOn = DateTime.UtcNow.AddSeconds(response.ExpiresInSeconds * 0.9);

            return _accessToken;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetToken().ConfigureAwait(false));
            if (RuntimeHelper.IsMono)
            {
                try
                {
                    var sendAsync = base.SendAsync(request, cancellationToken);
                    var result = await Task.WhenAny(sendAsync, Task.Delay(TimeSpan.FromSeconds(_config.FiskalyClientTimeout))).ConfigureAwait(false);
                    if (result == sendAsync)
                    {
                        return sendAsync.Result;
                    }
                    else
                    {
                        _logger.LogError("Task finish: " + HttpClientWrapper.Timoutlog);
                        throw new TimeoutException();
                    }
                }
                catch (Exception e)
                {
                    _logger?.LogError(e, e.Message);
                    if ((bool) (e.InnerException?.Message.Equals("A task was canceled.")))
                    {
                        _logger?.LogError(HttpClientWrapper.Timoutlog);
                        throw new TimeoutException("The client did not response in the configured time!");
                    }
                    throw new Exception(e.Message);
                }
            }
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private bool IsTokenExpired() => _expiresOn == null || _expiresOn < DateTime.UtcNow;
    }
}
