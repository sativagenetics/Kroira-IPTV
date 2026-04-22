#nullable enable
using System;
using System.Net;
using System.Net.Http;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public interface ISourceRoutingService
    {
        SourceRoutingDecision Resolve(SourceCredential? credential, SourceNetworkPurpose purpose);
        HttpClient CreateHttpClient(SourceCredential? credential, SourceNetworkPurpose purpose, TimeSpan timeout);
        void ApplyToPlaybackContext(SourceCredential? credential, PlaybackLaunchContext context);
    }

    public sealed class SourceRoutingService : ISourceRoutingService
    {
        public SourceRoutingDecision Resolve(SourceCredential? credential, SourceNetworkPurpose purpose)
        {
            if (credential == null || credential.ProxyScope == SourceProxyScope.Disabled)
            {
                return new SourceRoutingDecision
                {
                    Scope = SourceProxyScope.Disabled,
                    Summary = "Direct routing"
                };
            }

            var shouldUseProxy = ShouldUseProxy(credential.ProxyScope, purpose);
            var proxyUrl = NormalizeProxyUrl(credential.ProxyUrl);
            if (!shouldUseProxy || string.IsNullOrWhiteSpace(proxyUrl) || !Uri.TryCreate(proxyUrl, UriKind.Absolute, out _))
            {
                return new SourceRoutingDecision
                {
                    Scope = credential.ProxyScope,
                    ProxyUrl = proxyUrl,
                    UseProxy = false,
                    Summary = BuildSummary(credential.ProxyScope, proxyUrl, enabled: false)
                };
            }

            return new SourceRoutingDecision
            {
                Scope = credential.ProxyScope,
                ProxyUrl = proxyUrl,
                UseProxy = true,
                Summary = BuildSummary(credential.ProxyScope, proxyUrl, enabled: true)
            };
        }

        public HttpClient CreateHttpClient(SourceCredential? credential, SourceNetworkPurpose purpose, TimeSpan timeout)
        {
            var routing = Resolve(credential, purpose);
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                UseCookies = false
            };

            if (routing.UseProxy && Uri.TryCreate(routing.ProxyUrl, UriKind.Absolute, out var proxyUri))
            {
                handler.Proxy = new WebProxy(proxyUri);
                handler.UseProxy = true;
            }

            var client = new HttpClient(handler)
            {
                Timeout = timeout
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Kroira/1.0");
            return client;
        }

        public void ApplyToPlaybackContext(SourceCredential? credential, PlaybackLaunchContext context)
        {
            var routing = Resolve(credential, SourceNetworkPurpose.Playback);
            context.ProxyScope = routing.Scope;
            context.ProxyUrl = routing.UseProxy ? routing.ProxyUrl : string.Empty;
            context.RoutingSummary = routing.Summary;
        }

        private static bool ShouldUseProxy(SourceProxyScope scope, SourceNetworkPurpose purpose)
        {
            return scope switch
            {
                SourceProxyScope.PlaybackOnly => purpose == SourceNetworkPurpose.Playback,
                SourceProxyScope.PlaybackAndProbing => purpose is SourceNetworkPurpose.Playback or SourceNetworkPurpose.Probe,
                SourceProxyScope.AllRequests => true,
                _ => false
            };
        }

        private static string NormalizeProxyUrl(string? proxyUrl)
        {
            return string.IsNullOrWhiteSpace(proxyUrl) ? string.Empty : proxyUrl.Trim();
        }

        private static string BuildSummary(SourceProxyScope scope, string proxyUrl, bool enabled)
        {
            if (scope == SourceProxyScope.Disabled)
            {
                return "Direct routing";
            }

            if (!enabled)
            {
                return "Proxy configured but inactive";
            }

            var scopeLabel = scope switch
            {
                SourceProxyScope.PlaybackOnly => "Playback proxy",
                SourceProxyScope.PlaybackAndProbing => "Playback + probe proxy",
                SourceProxyScope.AllRequests => "Source-wide proxy",
                _ => "Proxy"
            };

            return string.IsNullOrWhiteSpace(proxyUrl)
                ? scopeLabel
                : $"{scopeLabel} via {proxyUrl}";
        }
    }
}
