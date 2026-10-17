using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    public sealed class TtsCacheLookupV1
    {
        public bool IsMiss { get; internal set; }
        public long RejectionEpoch { get; internal set; }
        public ArtifactV1 Artifact { get; internal set; }
        public string ReadUrl { get; internal set; }
        public string ExpiresAt { get; internal set; }
    }

    /// <summary>Pure strict parsers for the shared lookup and reconcile HTTP response DTOs.</summary>
    public static class TtsCacheHttpResponseV1
    {
        private const long MaxSafeInteger = 9007199254740991L;

        public static TtsCacheLookupV1 ParseLookup(string json, string expectedProfileId, string expectedKey)
        {
            var root = ParseObject(json);
            var miss = root.Property("miss", StringComparison.Ordinal);
            if (miss != null)
            {
                RequireFields(root, "miss", "rejection_epoch");
                if (miss.Value.Type != JTokenType.Boolean || !(bool)miss.Value)
                    throw new CacheValidationException("Lookup miss response must set miss=true.");
                return new TtsCacheLookupV1 { IsMiss = true, RejectionEpoch = ReadInteger(root["rejection_epoch"]) };
            }

            RequireFields(root, "artifact", "read_url", "expires_at");
            if (root["artifact"].Type != JTokenType.Object)
                throw new CacheValidationException("Lookup artifact must be an object.");
            var readUrl = ReadString(root, "read_url");
            if (!Uri.TryCreate(readUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new CacheValidationException("Lookup read URL must use HTTPS.");
            var expiresAt = ReadString(root, "expires_at");
            if (string.IsNullOrWhiteSpace(expiresAt)) throw new CacheValidationException("Lookup expiry is blank.");
            var artifact = TtsCacheContractsV1.ParseArtifact(root["artifact"].ToString(Formatting.None));
            if (artifact.profile_id != expectedProfileId || artifact.key != expectedKey)
                throw new CacheValidationException("Lookup artifact identity does not match its request.");
            return new TtsCacheLookupV1 { Artifact = artifact, ReadUrl = readUrl, ExpiresAt = expiresAt };
        }

        public static bool[] ParseReconcile(string json, IList<ArtifactV1> expectedArtifacts)
        {
            if (expectedArtifacts == null) throw new CacheValidationException("Expected artifacts are missing.");
            var root = ParseObject(json);
            RequireFields(root, "results");
            if (root["results"].Type != JTokenType.Array)
                throw new CacheValidationException("Reconcile results must be an array.");
            var rows = (JArray)root["results"];
            if (rows.Count != expectedArtifacts.Count)
                throw new CacheValidationException("Reconcile response does not cover the submitted inventory.");

            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < expectedArtifacts.Count; i++)
            {
                var artifact = expectedArtifacts[i];
                var identity = artifact == null ? null : Identity(artifact.profile_id, artifact.key, artifact.rejection_epoch);
                if (artifact == null || expected.ContainsKey(identity))
                    throw new CacheValidationException("Submitted reconciliation identities are not unique.");
                expected.Add(identity, i);
            }
            var matched = new HashSet<string>(StringComparer.Ordinal);
            var valid = new bool[expectedArtifacts.Count];
            foreach (var token in rows)
            {
                var row = token as JObject;
                RequireFields(row, "profile_id", "key", "rejection_epoch", "valid", "current_epoch");
                var profileId = ReadString(row, "profile_id");
                var key = ReadString(row, "key");
                var artifactEpoch = ReadInteger(row["rejection_epoch"]);
                var currentEpoch = ReadInteger(row["current_epoch"]);
                if (row["valid"].Type != JTokenType.Boolean)
                    throw new CacheValidationException("Reconcile valid flag must be Boolean.");
                var identity = Identity(profileId, key, artifactEpoch);
                if (!expected.TryGetValue(identity, out var index) || !matched.Add(identity))
                    throw new CacheValidationException("Reconcile row identity is unexpected or duplicated.");
                valid[index] = (bool)row["valid"] && currentEpoch == artifactEpoch;
            }
            if (matched.Count != expected.Count)
                throw new CacheValidationException("Reconcile response omitted an inventory identity.");
            return valid;
        }

        private static JObject ParseObject(string json)
        {
            try
            {
                using (var text = new StringReader(json ?? string.Empty))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    var value = JObject.Load(reader, new JsonLoadSettings
                    { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new CacheValidationException("Trailing JSON content.");
                    return value;
                }
            }
            catch (CacheValidationException) { throw; }
            catch (Exception exception) { throw new CacheValidationException("Invalid HTTP response JSON: " + exception.Message); }
        }

        private static void RequireFields(JObject value, params string[] expected)
        {
            if (value == null || value.Properties().Count() != expected.Length ||
                expected.Any(name => value.Property(name, StringComparison.Ordinal) == null))
                throw new CacheValidationException("HTTP response fields do not match the cache contract.");
        }

        private static string ReadString(JObject value, string field)
        {
            if (value[field]?.Type != JTokenType.String) throw new CacheValidationException("HTTP response field must be a string: " + field);
            return (string)value[field];
        }

        private static long ReadInteger(JToken value)
        {
            if (value?.Type != JTokenType.Integer) throw new CacheValidationException("HTTP response epoch must be an integer.");
            var integer = value.Value<long>();
            if (integer < 0 || integer > MaxSafeInteger) throw new CacheValidationException("HTTP response epoch is out of range.");
            return integer;
        }

        private static string Identity(string profileId, string key, long epoch) => profileId + "\n" + key + "\n" + epoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
