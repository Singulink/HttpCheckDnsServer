using DNS.Client.RequestResolver;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpCheckDnsServer
{
    public class ResponseEventArgs : EventArgs
    {
        public ResponseEventArgs(int id, string requestDomain, string emailDomain, ResponseResult result, TimeSpan ttl)
        {
            Id = id;
            RequestDomain = requestDomain;
            EmailDomain = emailDomain;
            Result = result;
            Ttl = ttl;
        }

        public int Id { get; set; }
        public string RequestDomain { get; set; }
        public string EmailDomain { get; set; }
        public ResponseResult Result { get; set; }
        public TimeSpan Ttl { get; set; }
    }
}
