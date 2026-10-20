using Newtonsoft.Json.Linq;
using System;

namespace YolanLive.Core.Helper
{
    public enum LiveFailureKind { Authentication, Network, Platform }
    public sealed class LiveRequestException : Exception
    {
        public LiveFailureKind Kind { get; }
        public LiveRequestException(LiveFailureKind kind, string message) : base(message) { Kind = kind; }
        public static void EnsureSuccess(JObject response, string codeField, bool bilibili = false)
        {
            var code = response[codeField]?.ToObject<int>() ?? 0;
            if (code == 0) return;
            var authentication = code == 401 || (bilibili && code == -101);
            throw new LiveRequestException(authentication ? LiveFailureKind.Authentication : LiveFailureKind.Platform,
                authentication ? "平台要求重新登录" : "平台接口暂不可用");
        }
    }
}
