using DNS.Client.RequestResolver;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HttpCheckDnsServer
{
    public class RequestResolver : IRequestResolver
    {
        private static readonly MemoryCache s_cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000000 });

        private static readonly MemoryCacheEntryOptions s_entryOptions = new MemoryCacheEntryOptions {
            Size = 1,
            SlidingExpiration = TimeSpan.FromDays(30),
            PostEvictionCallbacks = { new PostEvictionCallbackRegistration() { EvictionCallback = PostEvictionDelegate } }
        };

        private static readonly MemoryCacheEntryOptions s_permanentEntryOptions = new MemoryCacheEntryOptions
        {
            Size = 1,
            Priority = CacheItemPriority.NeverRemove,
            AbsoluteExpiration = DateTime.Now.AddYears(1000),
        };

        private static readonly IPAddress s_loopbackIpAddress = IPAddress.Parse("127.0.0.1");

        private readonly string _requestDomainSuffix;
        private readonly Domain _dnsServerDomain;
        private readonly Domain _responsiblePersonDomain;
        private readonly long _serial;

        public event EventHandler<RequestEventArgs> RequestReceived;
        public event EventHandler<ResponseEventArgs> ResponseSent;

        public RequestResolver(string dnsServerAddress, string responsiblePerson)
        {
            dnsServerAddress = dnsServerAddress.ToLower();

            _requestDomainSuffix = '.' + dnsServerAddress;
            _dnsServerDomain = new Domain(dnsServerAddress);
            _responsiblePersonDomain = new Domain(responsiblePerson);
            _serial = DateTime.UtcNow.Ticks;
        }

        public void AddPermanentRecord(string domain, bool isValid) => s_cache.Set(domain, new DomainState(domain, isValid), s_permanentEntryOptions);

        public Task<IResponse> Resolve(IRequest request)
        {
            IResponse response = Response.FromRequest(request);
            response.AuthorativeServer = true;

            foreach (Question question in response.Questions) {
                if (question.Type == RecordType.A) {
                    string requestDomain = question.Name.ToString().ToLower();
                    string emailDomain = GetEmailDomain(requestDomain);

                    RequestReceived?.Invoke(this, new RequestEventArgs(response.Id, requestDomain, emailDomain));

                    if (emailDomain != null) {
                        var (isValid, ttl) = GetEmailDomainState(emailDomain);

                        if (isValid) {
                            var soaRecord = new StartOfAuthorityResourceRecord(_dnsServerDomain, _dnsServerDomain, _responsiblePersonDomain, _serial,
                                TimeSpan.FromDays(2), TimeSpan.FromDays(1), TimeSpan.FromDays(30), ttl, TimeSpan.FromDays(14));

                            response.AuthorityRecords.Add(soaRecord);
                            response.ResponseCode = ResponseCode.NameError;

                            ResponseSent?.Invoke(this, new ResponseEventArgs(response.Id, requestDomain, emailDomain, ResponseResult.Valid, ttl));
                        }
                        else {
                            var record = new IPAddressResourceRecord(question.Name, s_loopbackIpAddress, ttl);
                            response.AnswerRecords.Add(record);

                            ResponseSent?.Invoke(this, new ResponseEventArgs(response.Id, requestDomain, emailDomain, ResponseResult.Invalid, ttl));
                        }
                    }
                    else {
                        // Invalid domain name request, cache for 1000 days
                        var ttl = TimeSpan.FromDays(1000);
                        var soaRecord = new StartOfAuthorityResourceRecord(question.Name, _dnsServerDomain, _responsiblePersonDomain, _serial,
                            TimeSpan.FromDays(2), TimeSpan.FromDays(1), TimeSpan.FromDays(30), ttl, ttl);

                        response.AuthorityRecords.Add(soaRecord);
                        response.ResponseCode = ResponseCode.FormatError;

                        ResponseSent?.Invoke(this, new ResponseEventArgs(response.Id, requestDomain, emailDomain, ResponseResult.Error, ttl));
                    }
                }
            }

            return Task.FromResult(response);
        }

        private string GetEmailDomain(string requestDomain)
        {
            if (!requestDomain.EndsWith(_requestDomainSuffix))
                return null;

            string emailDomain = requestDomain.Substring(0, requestDomain.Length - _requestDomainSuffix.Length);
            return emailDomain.Contains('.') ? emailDomain : null;
        }

        private string[] GetTestDomains(string emailDomain)
        {
            string[] emailDomainNameParts = emailDomain.Split('.', StringSplitOptions.RemoveEmptyEntries);
            int testDomainCount = emailDomainNameParts.Length - 1;

            string[] testDomains = new string[testDomainCount];

            for (int numParts = 2; numParts < emailDomainNameParts.Length; numParts++) {
                testDomains[numParts - 2] = string.Join('.', emailDomainNameParts, emailDomainNameParts.Length - numParts, numParts);
            }

            testDomains[testDomainCount - 1] = emailDomain;

            return testDomains;
        }

        private (bool IsValid, TimeSpan Ttl) GetEmailDomainState(string emailDomain)
        {
            string[] testDomains = GetTestDomains(emailDomain);

            List<string> newTestDomains = null;
            var maxValidTtl = TimeSpan.Zero;
            var minInvalidTtl = TimeSpan.MaxValue;

            DomainState domainState = null;

            foreach (string testDomain in testDomains) {
                if (s_cache.TryGetValue(testDomain, out domainState)) {
                    if (domainState.IsValid) {
                        if (domainState.Ttl > maxValidTtl)
                            maxValidTtl = domainState.Ttl;
                    }
                    else if (domainState.Ttl < minInvalidTtl) {
                        minInvalidTtl = domainState.Ttl;
                    }
                }
                else {
                    if (newTestDomains == null)
                        newTestDomains = new List<string>(testDomains.Length);

                    newTestDomains.Add(testDomain);
                }
            }

            if (maxValidTtl > TimeSpan.Zero)
                return (true, maxValidTtl);

            if (newTestDomains == null)
                return (false, minInvalidTtl);

            foreach (string newTestDomain in newTestDomains) {
                domainState = new DomainState(newTestDomain);
                s_cache.Set(newTestDomain, domainState, s_entryOptions);
            }

            return (true, domainState.Ttl);
        }

        private static void PostEvictionDelegate(object key, object value, EvictionReason reason, object state)
        {
            var domainState = (DomainState)value;
            domainState.CancelRefreshLoop();
        }
    }
}
