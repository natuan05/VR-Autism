using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Remote
{
    [DisallowMultipleComponent]
    public sealed class LiveKitLessonRemoteBridgeV2 : MonoBehaviour, IDisposable
    {
        [Serializable]
        private sealed class LessonStateRequestV2
        {
            public int contract_version;
            public string @event;
            public string session_id;
        }

        private sealed class ParsedStateRequest
        {
            public string sessionId;
        }

        private static readonly string[] StateRequestFields = { "contract_version", "event", "session_id" };
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private ILiveKitDataPacketClientV2 _client;
        private LessonGraphRunner _runner;
        private string _sessionId = string.Empty;
        private volatile bool _disposed;
        private int _configurationGeneration;
        private Action<byte[], string> _dataHandler;
        private Action _reconnectHandler;
        private Action<LessonStateV2> _stateHandler;

        internal bool IsConfigured => !_disposed && _client != null && _runner != null &&
            !string.IsNullOrWhiteSpace(_sessionId);

        public void Configure(ILiveKitDataPacketClientV2 client, LessonGraphRunner runner)
        {
            var generation = Interlocked.Increment(ref _configurationGeneration);
            Detach();
            DrainQueue();

            _disposed = false;
            _runner = runner;
            _client = client;
            _sessionId = SessionContext.Instance?.SessionId ?? string.Empty;

            VoicePhraseSessionMetadataV2 phraseMetadata;
            if (_client == null || _runner == null || string.IsNullOrWhiteSpace(_sessionId) ||
                !VoicePhraseSnapshotStoreV2.TryGetSessionMetadata(out phraseMetadata))
            {
                _client = null;
                _sessionId = string.Empty;
                return;
            }

            _dataHandler = (data, topic) => OnDataReceived(data, topic, generation);
            _reconnectHandler = () => OnReconnected(generation);
            _stateHandler = state => OnStateChanged(state, generation);
            _client.DataReceivedV2 += _dataHandler;
            _client.ReconnectedV2 += _reconnectHandler;
            _runner.StateChanged += _stateHandler;
            PublishCurrentState();
        }

        private void Update()
        {
            while (!_disposed && _mainThreadQueue.TryDequeue(out var callback))
            {
                try { callback(); }
                catch (Exception exception) { Debug.LogError($"[LessonGraphV2] Remote bridge callback failed: {exception}", this); }
            }
        }

        private void OnDataReceived(byte[] data, string topic, int generation)
        {
            if (!IsCurrent(generation) || !string.Equals(topic, LessonRemoteContractV2.RemoteTopic, StringComparison.Ordinal) || data == null)
                return;

            var payload = (byte[])data.Clone();
            _mainThreadQueue.Enqueue(() =>
            {
                if (IsCurrent(generation)) HandlePacket(payload, generation);
            });
        }

        private void HandlePacket(byte[] payload, int generation)
        {
            if (!IsCurrent(generation)) return;

            ParsedStateRequest stateRequest;
            if (TryParseStateRequest(payload, out stateRequest))
            {
                HandleStateRequest(stateRequest);
                return;
            }

            LessonCommandV2 command;
            string reason;
            if (!LessonCommandCodecV2.TryParse(payload, out command, out reason))
                return;

            HandleCommand(command, generation);
        }

        private void OnReconnected(int generation)
        {
            if (!IsCurrent(generation)) return;
            _mainThreadQueue.Enqueue(() => { if (IsCurrent(generation)) PublishCurrentState(); });
        }

        private void OnStateChanged(LessonStateV2 state, int generation)
        {
            if (!IsCurrent(generation) || state == null) return;
            var snapshot = CloneState(state);
            _mainThreadQueue.Enqueue(() => { if (IsCurrent(generation)) PublishState(snapshot); });
        }

        private void HandleCommand(LessonCommandV2 command, int generation)
        {
            if (!IsCurrent(generation) || _client == null || _runner == null || !_client.IsConnectedV2)
                return;

            if (!string.Equals(command.session_id, _sessionId, StringComparison.Ordinal))
            {
                PublishResult(CreateResult(command, false, LessonCommandReasonV2.WrongSession, _runner.CurrentState));
                return;
            }

            // Apply only on the Unity thread and let the runner compare run/node/activation
            // against its current snapshot. This catches packets that went stale while queued.
            Task<LessonCommandResultV2> task;
            try { task = _runner.ApplyCommandAsync(command); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LessonGraphV2] Remote command could not be applied: {exception.Message}", this);
                return;
            }

            if (task.IsCompleted)
            {
                PublishCompletedCommand(task);
                return;
            }

            task.ContinueWith(
                completed =>
                {
                    if (!IsCurrent(generation)) return;
                    LessonCommandResultV2 result;
                    try { result = completed.GetAwaiter().GetResult(); }
                    catch (Exception exception)
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            if (IsCurrent(generation)) Debug.LogWarning($"[LessonGraphV2] Remote command task failed: {exception.Message}", this);
                        });
                        return;
                    }
                    if (!IsCurrent(generation)) return;
                    _mainThreadQueue.Enqueue(() => { if (IsCurrent(generation)) PublishResult(result); });
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void PublishCompletedCommand(Task<LessonCommandResultV2> task)
        {
            try { PublishResult(task.GetAwaiter().GetResult()); }
            catch (Exception exception) { Debug.LogWarning($"[LessonGraphV2] Remote command task failed: {exception.Message}", this); }
        }

        private void HandleStateRequest(ParsedStateRequest request)
        {
            if (_disposed || _client == null || _runner == null || !_client.IsConnectedV2 ||
                !string.Equals(request.sessionId, _sessionId, StringComparison.Ordinal))
                return;

            var state = _runner.CurrentState;
            if (state == null || !string.Equals(state.session_id, _sessionId, StringComparison.Ordinal))
                return;
            PublishState(state);
        }

        private void PublishCurrentState()
        {
            if (_disposed || _runner == null) return;
            PublishState(_runner.CurrentState);
        }

        private void PublishState(LessonStateV2 state)
        {
            if (_disposed || _client == null || !_client.IsConnectedV2 || state == null ||
                !string.Equals(state.session_id, _sessionId, StringComparison.Ordinal))
                return;

            var envelope = new LessonStatePacketV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.StateEvent,
                state = CloneState(state)
            };
            Publish(JsonUtility.ToJson(envelope));
        }

        private void PublishResult(LessonCommandResultV2 result)
        {
            if (_disposed || _client == null || !_client.IsConnectedV2 || result == null)
                return;
            Publish(JsonUtility.ToJson(result));
        }

        private void Publish(string json)
        {
            if (_disposed || _client == null || !_client.IsConnectedV2) return;
            _client.PublishDataV2(Encoding.UTF8.GetBytes(json), LessonRemoteContractV2.RemoteTopic, true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Interlocked.Increment(ref _configurationGeneration);
            _disposed = true;
            Detach();
            DrainQueue();
        }

        private void OnDisable() => Dispose();
        private void OnDestroy() => Dispose();

        private void Detach()
        {
            if (_client != null)
            {
                if (_dataHandler != null) _client.DataReceivedV2 -= _dataHandler;
                if (_reconnectHandler != null) _client.ReconnectedV2 -= _reconnectHandler;
            }
            if (_runner != null)
            {
                if (_stateHandler != null) _runner.StateChanged -= _stateHandler;
            }
            _client = null;
            _runner = null;
            _sessionId = string.Empty;
            _dataHandler = null;
            _reconnectHandler = null;
            _stateHandler = null;
        }

        private bool IsCurrent(int generation) => !_disposed && generation == Volatile.Read(ref _configurationGeneration);

        private void DrainQueue()
        {
            while (_mainThreadQueue.TryDequeue(out _)) { }
        }

        private static LessonCommandResultV2 CreateResult(LessonCommandV2 command, bool accepted, string reason, LessonStateV2 state)
        {
            return new LessonCommandResultV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandResultEvent,
                command_id = command.command_id,
                session_id = command.session_id,
                run_id = command.run_id,
                node_id = command.node_id,
                activation_id = command.activation_id,
                command = command.command,
                binding_id = command.binding_id,
                accepted = accepted,
                reason = reason,
                state = CloneState(state)
            };
        }

        private static LessonStateV2 CloneState(LessonStateV2 state)
        {
            if (state == null) return null;
            var bindings = state.bindings == null ? Array.Empty<LessonBindingV2>() : new LessonBindingV2[state.bindings.Length];
            if (state.bindings != null)
            {
                for (var index = 0; index < state.bindings.Length; index++)
                {
                    var binding = state.bindings[index];
                    if (binding == null) continue;
                    bindings[index] = new LessonBindingV2
                    {
                        binding_id = binding.binding_id,
                        npc_binding_id = binding.npc_binding_id,
                        can_verbal_hint = binding.can_verbal_hint,
                        can_visual_hint = binding.can_visual_hint
                    };
                }
            }

            return new LessonStateV2
            {
                contract_version = state.contract_version,
                session_id = state.session_id,
                run_id = state.run_id,
                graph_id = state.graph_id,
                lesson_id = state.lesson_id,
                launch_token = state.launch_token,
                lesson_voice_revision = state.lesson_voice_revision,
                child_phrase_revision = state.child_phrase_revision,
                node_id = state.node_id,
                node_type = state.node_type,
                node_index = state.node_index,
                activation_id = state.activation_id,
                status = state.status,
                checkpoint_id = state.checkpoint_id,
                updated_at_utc = state.updated_at_utc,
                state_revision = state.state_revision,
                active_node_ids = state.active_node_ids == null ? Array.Empty<string>() : (string[])state.active_node_ids.Clone(),
                parallel_group_id = state.parallel_group_id,
                bindings = bindings
            };
        }

        private static bool TryParseStateRequest(byte[] payload, out ParsedStateRequest request)
        {
            request = null;
            if (payload == null || payload.Length == 0) return false;

            string json;
            try { json = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return false; }

            List<string> members;
            if (!TrySplitObjectMembers(json, out members) || members.Count != StateRequestFields.Length)
                return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < members.Count; index++)
            {
                var colon = FindMemberColon(members[index]);
                if (colon < 0) return false;
                var key = members[index].Substring(0, colon).Trim();
                if (Array.IndexOf(StateRequestFields, key.Trim('"')) < 0 || key.Length < 2 ||
                    key[0] != '"' || key[key.Length - 1] != '"' || !seen.Add(key.Trim('"')))
                    return false;

                var value = members[index].Substring(colon + 1).Trim();
                if (key == "\"contract_version\"" && value != "2") return false;
                if ((key == "\"event\"" || key == "\"session_id\"") &&
                    (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"'))
                    return false;
            }

            if (seen.Count != StateRequestFields.Length) return false;
            LessonStateRequestV2 parsed;
            try { parsed = JsonUtility.FromJson<LessonStateRequestV2>(json); }
            catch { return false; }
            if (parsed == null || parsed.contract_version != LessonRemoteContractV2.ContractVersion ||
                parsed.@event != "LESSON_STATE_REQUEST" || string.IsNullOrWhiteSpace(parsed.session_id))
                return false;

            request = new ParsedStateRequest { sessionId = parsed.session_id };
            return true;
        }

        private static bool TrySplitObjectMembers(string json, out List<string> members)
        {
            members = null;
            if (json == null) return false;
            var start = 0;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            var end = json.Length - 1;
            while (end >= start && char.IsWhiteSpace(json[end])) end--;
            if (end <= start || json[start] != '{' || json[end] != '}') return false;

            var found = new List<string>();
            var memberStart = start + 1;
            var inString = false;
            var escaped = false;
            var nesting = 0;
            for (var index = start + 1; index < end; index++)
            {
                var value = json[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') inString = false;
                    continue;
                }
                if (value == '"') { inString = true; continue; }
                if (value == '{' || value == '[') { nesting++; continue; }
                if (value == '}' || value == ']')
                {
                    nesting--;
                    if (nesting < 0) return false;
                    continue;
                }
                if (value == ',' && nesting == 0)
                {
                    found.Add(json.Substring(memberStart, index - memberStart).Trim());
                    memberStart = index + 1;
                }
            }
            if (inString || escaped || nesting != 0) return false;

            var finalMember = json.Substring(memberStart, end - memberStart).Trim();
            if (finalMember.Length > 0) found.Add(finalMember);
            else if (found.Count > 0) return false;
            members = found;
            return true;
        }

        private static int FindMemberColon(string member)
        {
            var inString = false;
            var escaped = false;
            var nesting = 0;
            for (var index = 0; index < member.Length; index++)
            {
                var value = member[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') inString = false;
                    continue;
                }
                if (value == '"') { inString = true; continue; }
                if (value == '{' || value == '[') nesting++;
                else if (value == '}' || value == ']') nesting--;
                else if (value == ':' && nesting == 0) return index;
            }
            return -1;
        }
    }
}
