using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_EDITOR
using System.Net.Http;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    /// <summary>Small HTTPS adapter for the session, reconcile and lookup API.</summary>
    public sealed class TtsCacheBackendClientV1
    {
        public const int SessionRequestTimeoutMs = 5000;
        public const int ReconcileRequestTimeoutMs = 5000;
        public const int BackgroundWarmTimeoutMs = 10000;
        private readonly string _baseUrl;
        private readonly string _bearer;
#if UNITY_EDITOR
        private static readonly HttpClient LocalBackendHttpClient = CreateLocalBackendHttpClient();
#endif

        public TtsCacheBackendClientV1(string baseUrl, string bearerToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Configure the cache backend URL for this build/device.", nameof(baseUrl));
            if (!TtsCacheBackendUrlPolicyV1.IsAllowedBackendUrl(baseUrl.TrimEnd('/'), IsEditorBuild()))
                throw new ArgumentException("TTS cache backend must be an HTTPS origin or an Editor-only localhost HTTP origin without credentials/query/fragment.", nameof(baseUrl));
            var baseUri = new Uri(baseUrl.TrimEnd('/'), UriKind.Absolute);
            _baseUrl = baseUri.AbsoluteUri.TrimEnd('/');
            _bearer = bearerToken ?? string.Empty;
        }

        public async Task<SessionTtsSnapshotV1> GetRetainedSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var url = _baseUrl + "/api/tts-cache/session?session_id=" + UnityWebRequest.EscapeURL(sessionId ?? string.Empty);
            var request = UnityWebRequest.Get(url);
            request.downloadHandler = new BoundedDownloadHandler(256 * 1024);
            var response = await SendAsync(request, cancellationToken, 256 * 1024, true, true, SessionRequestTimeoutMs);
            return SessionTtsSnapshotV1.Parse(response, sessionId);
        }

        public async Task<bool[]> ReconcileAsync(string sessionId, IList<ArtifactV1> artifacts, CancellationToken cancellationToken)
        {
            var result = new bool[artifacts == null ? 0 : artifacts.Count];
            if (result.Length == 0) return result;
            var complete = true;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(ReconcileRequestTimeoutMs);
                for (var start = 0; start < result.Length; start += 128)
                {
                    if (deadline.IsCancellationRequested)
                    {
                        if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                        complete = false; break;
                    }
                    var count = Math.Min(128, result.Length - start);
                    var body = new JObject
                    {
                        ["session_id"] = sessionId,
                        ["artifacts"] = new JArray(BuildArtifactTokens(artifacts, start, count))
                    };
                    try
                    {
                        var response = await SendJsonAsync("/api/tts-cache/reconcile", body, deadline.Token, ReconcileRequestTimeoutMs);
                        var batch = new List<ArtifactV1>(count);
                        for (var i = start; i < start + count; i++) batch.Add(artifacts[i]);
                        var validity = TtsCacheHttpResponseV1.ParseReconcile(response, batch);
                        Array.Copy(validity, 0, result, start, count);
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                        complete = false; break;
                    }
                    catch { complete = false; /* An unverified launch inventory entry remains a miss. */ }
                }
            }
            return complete ? result : null;
        }

        public async Task<TtsCacheLookupV1> LookupAsync(string sessionId, string profileId, string key, CancellationToken cancellationToken,
            int timeoutMs = SessionRequestTimeoutMs)
        {
            var body = new JObject { ["session_id"] = sessionId, ["profile_id"] = profileId, ["key"] = key };
            var json = await SendJsonAsync("/api/tts-cache/lookup", body, cancellationToken, timeoutMs);
            return TtsCacheHttpResponseV1.ParseLookup(json, profileId, key);
        }

        public async Task<byte[]> DownloadAsync(string readUrl, CancellationToken cancellationToken, int timeoutMs = BackgroundWarmTimeoutMs)
        {
            if (!TtsCacheBackendUrlPolicyV1.IsHttpsReadUrl(readUrl))
                throw new CacheValidationException("Read URL must be a valid HTTPS Storage URL.");
            var request = UnityWebRequest.Get(readUrl);
            var handler = new BoundedDownloadHandler(TtsWavValidatorV1.MaxWavBytes);
            request.downloadHandler = handler;
            await SendAsync(request, cancellationToken, TtsWavValidatorV1.MaxWavBytes, false, false, timeoutMs);
            if (handler.TooLarge) throw new CacheValidationException("Remote cache artifact exceeded WAV limit.");
            return handler.Bytes;
        }

        private async Task<string> SendJsonAsync(string path, JObject body, CancellationToken cancellationToken, int timeoutMs)
        {
            var request = new UnityWebRequest(_baseUrl + path, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
            request.downloadHandler = new BoundedDownloadHandler(256 * 1024);
            request.SetRequestHeader("Content-Type", "application/json");
            return await SendAsync(request, cancellationToken, 256 * 1024, true, true, timeoutMs);
        }

        internal static bool ShouldAttachBearer(string requestUrl, string apiBaseUrl, bool backendRequest)
        {
            if (!backendRequest || !Uri.TryCreate(requestUrl, UriKind.Absolute, out var requestUri) ||
                !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri) ||
                !string.IsNullOrEmpty(requestUri.UserInfo) || !string.IsNullOrEmpty(apiUri.UserInfo) ||
                !TtsCacheBackendUrlPolicyV1.IsAllowedBackendUrl(apiBaseUrl, IsEditorBuild())) return false;
            return requestUri.Scheme.Equals(apiUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   requestUri.Host.Equals(apiUri.Host, StringComparison.OrdinalIgnoreCase) &&
                   requestUri.Port == apiUri.Port;
        }

        internal static string DecodeJsonBytes(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes ?? Array.Empty<byte>());

        private async Task<string> SendAsync(UnityWebRequest request, CancellationToken cancellationToken, int maxBytes, bool backendRequest, bool decodeText, int timeoutMs)
        {
            using (request)
            {
#if UNITY_EDITOR
                if (backendRequest && TtsCacheBackendUrlPolicyV1.IsEditorLocalBackend(_baseUrl))
                    return await SendLocalDevelopmentBackendAsync(request, cancellationToken, maxBytes, decodeText, timeoutMs);
#endif
                if (ShouldAttachBearer(request.url, _baseUrl, backendRequest) && !string.IsNullOrEmpty(_bearer))
                    request.SetRequestHeader("Authorization", "Bearer " + _bearer);
                // Neither backend bearer requests nor signed Storage reads may cross a redirect boundary.
                request.redirectLimit = 0;
                request.timeout = Math.Max(1, (Math.Max(1, timeoutMs) + 999) / 1000);
                using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    bounded.CancelAfter(Math.Max(1, timeoutMs));
                    var operation = request.SendWebRequest();
                    using (bounded.Token.Register(() => { try { request.Abort(); } catch { } }))
                    {
                        while (!operation.isDone)
                        {
                            bounded.Token.ThrowIfCancellationRequested();
                            await Task.Yield();
                        }
                    }
                    bounded.Token.ThrowIfCancellationRequested();
                }
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("TTS cache backend request failed (" + request.responseCode + ").");
                if (request.downloadedBytes > (ulong)maxBytes) throw new CacheValidationException("TTS cache response exceeded its size limit.");
                if (!decodeText) return string.Empty;
                if (request.downloadHandler is BoundedDownloadHandler responseHandler) return responseHandler.GetUtf8Text();
                return request.downloadHandler.text;
            }
        }

#if UNITY_EDITOR
        private static HttpClient CreateLocalBackendHttpClient()
        {
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private async Task<string> SendLocalDevelopmentBackendAsync(UnityWebRequest request, CancellationToken cancellationToken,
            int maxBytes, bool decodeText, int timeoutMs)
        {
            using (var message = new HttpRequestMessage(new HttpMethod(request.method), request.url))
            {
                var uploadBytes = request.uploadHandler == null ? null : request.uploadHandler.data;
                if (uploadBytes != null)
                {
                    message.Content = new ByteArrayContent(uploadBytes);
                    var contentType = request.GetRequestHeader("Content-Type");
                    if (!string.IsNullOrEmpty(contentType)) message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
                if (ShouldAttachBearer(request.url, _baseUrl, true) && !string.IsNullOrEmpty(_bearer))
                    message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _bearer);

                using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    bounded.CancelAfter(Math.Max(1, timeoutMs));
                    using (var response = await LocalBackendHttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, bounded.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("TTS cache backend request failed (" + (long)response.StatusCode + ").");
                        if (response.Content.Headers.ContentLength > maxBytes)
                            throw new CacheValidationException("TTS cache response exceeded its size limit.");
                        if (!decodeText) return string.Empty;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var output = new MemoryStream(Math.Min(maxBytes, 8192)))
                        {
                            var buffer = new byte[8192];
                            while (true)
                            {
                                bounded.Token.ThrowIfCancellationRequested();
                                var count = await stream.ReadAsync(buffer, 0, buffer.Length, bounded.Token);
                                if (count == 0) break;
                                if (output.Length + count > maxBytes)
                                    throw new CacheValidationException("TTS cache response exceeded its size limit.");
                                output.Write(buffer, 0, count);
                            }
                            return DecodeJsonBytes(output.ToArray());
                        }
                    }
                }
            }
        }

        private static bool IsEditorBuild() => true;
#else
        private static bool IsEditorBuild() => false;
#endif

        private static IEnumerable<JObject> BuildArtifactTokens(IList<ArtifactV1> artifacts, int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var a = artifacts[i];
                yield return new JObject
                {
                    ["profile_id"] = a.profile_id, ["key"] = a.key, ["rejection_epoch"] = a.rejection_epoch,
                    ["object_path"] = a.object_path, ["generation"] = a.generation, ["sha256"] = a.sha256,
                    ["byte_size"] = a.byte_size, ["sample_count"] = a.sample_count,
                    ["duration_ms"] = a.duration_ms, ["format"] = a.format
                };
            }
        }

        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private readonly int _maximum;
            private readonly List<byte> _bytes = new List<byte>();
            public bool TooLarge { get; private set; }
            public byte[] Bytes => _bytes.ToArray();
            public string GetUtf8Text() => DecodeJsonBytes(_bytes.ToArray());
            public BoundedDownloadHandler(int maximum) : base(new byte[8192]) { _maximum = maximum; }
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                if (_bytes.Count + dataLength > _maximum) { TooLarge = true; return false; }
                for (var i = 0; i < dataLength; i++) _bytes.Add(data[i]);
                return true;
            }
        }
    }
}
