using System;
using System.Net;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    /// <summary>Restricts the local development backend exception to one explicit Editor setting.</summary>
    internal static class TtsCacheBackendUrlPolicyV1
    {
        internal const string EditorPreferenceKey = "VRAutism.TtsCache.UseLocalDevelopmentBackendV1";
        internal const string LocalDevelopmentBackendUrl = "http://127.0.0.1:3000";

        internal static string ResolveBackendUrl(string configuredUrl)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(EditorPreferenceKey, false)) return LocalDevelopmentBackendUrl;
#endif
            return configuredUrl;
        }

        internal static bool IsAllowedBackendUrl(string url, bool editorBuild)
        {
            if (!TryGetOrigin(url, out var uri)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return editorBuild || !IsLoopbackHost(uri.Host);
            if (uri.Scheme != Uri.UriSchemeHttp || !editorBuild) return false;
            return IsExplicitEditorLoopbackHost(url, uri);
        }

        internal static bool IsEditorLocalBackend(string url)
        {
#if UNITY_EDITOR
            return TryGetOrigin(url, out var uri) && uri.Scheme == Uri.UriSchemeHttp &&
                   IsExplicitEditorLoopbackHost(url, uri);
#else
            return false;
#endif
        }

        internal static bool IsHttpsReadUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
                   string.IsNullOrEmpty(uri.Fragment);
        }

        private static bool TryGetOrigin(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url) || !string.Equals(url, url.Trim(), StringComparison.Ordinal)) return false;
            var schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator < 0) return false;
            var authorityStart = schemeSeparator + 3;
            var authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) authorityEnd = url.Length;
            var authority = url.Substring(authorityStart, authorityEnd - authorityStart);
            if (string.IsNullOrEmpty(authority) || authority.EndsWith(":", StringComparison.Ordinal)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment)) return false;
            uri = parsed;
            return true;
        }

        private static bool IsExplicitEditorLoopbackHost(string url, Uri uri)
        {
            if (uri.HostNameType == UriHostNameType.IPv6) return false;

            // Use the original authority too: Uri may normalize shorthand or integer-form IPv4 hosts.
            var schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator < 0) return false;
            var authorityStart = schemeSeparator + 3;
            var authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) authorityEnd = url.Length;
            var authority = url.Substring(authorityStart, authorityEnd - authorityStart);
            if (authority.IndexOf('@') >= 0 || authority.StartsWith("[", StringComparison.Ordinal)) return false;
            var portSeparator = authority.LastIndexOf(':');
            var originalHost = portSeparator < 0 ? authority : authority.Substring(0, portSeparator);

            return originalHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   originalHost.Equals("127.0.0.1", StringComparison.Ordinal);
        }

        private static bool IsLoopbackHost(string host)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.TrimEnd('.').Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host.Substring(1, host.Length - 2);
            if (!IPAddress.TryParse(host, out var address)) return false;
            return IPAddress.IsLoopback(address) ||
                   (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4()));
        }
    }
}
