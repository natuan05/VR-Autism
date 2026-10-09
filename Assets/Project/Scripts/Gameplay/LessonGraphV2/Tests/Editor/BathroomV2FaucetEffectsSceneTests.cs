using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class BathroomV2FaucetEffectsSceneTests
    {
        private const string SceneFileName = "Bathroom-V2.unity";
        private const string FaucetScriptGuid = "f407129f5c83411da1b74e8e9f39ea32";
        private const string WaterClipGuid = "d2cd9b723149e6f438a81bad51e82cb2";
        private const string FaucetId = "6200000000000003001";
        private const string AudioSourceId = "6200000000000003002";
        private const string WaterTapId = "652140273";
        private const string TapRootId = "652140269";

        private static string ReadScene()
        {
            string path = Path.Combine(UnityEngine.Application.dataPath, "Project", "Scenes", SceneFileName);
            Assert.That(File.Exists(path), Is.True, "Bathroom-V2 scene should exist at " + path);
            return File.ReadAllText(path);
        }

        [Test]
        public void FaucetEffects_UsesPrefabRootAndDedicatedLocalAudioSource()
        {
            string scene = ReadScene();
            string faucet = GetDocument(scene, FaucetId);
            string audio = GetDocument(scene, AudioSourceId);
            string prefabInstance = GetDocument(scene, "652140266");

            StringAssert.Contains("guid: " + FaucetScriptGuid, faucet);
            StringAssert.Contains("Assembly-CSharp::VRAutism.Gameplay.LessonGraphV2.Integration.FaucetEffectsV2", faucet);
            StringAssert.Contains("m_GameObject: {fileID: " + TapRootId + "}", faucet);
            StringAssert.Contains("_runner: {fileID: 1130038378}", faucet);
            StringAssert.Contains("_tapAnimator: {fileID: 652140272}", faucet);
            StringAssert.Contains("_runningWater: {fileID: 1586076941}", faucet);
            StringAssert.Contains("_waterAudioSource: {fileID: " + AudioSourceId + "}", faucet);
            StringAssert.Contains("guid: " + WaterClipGuid, faucet);
            StringAssert.Contains("Assembly-CSharp::VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphRunner",
                GetDocument(scene, "1130038378"));

            StringAssert.Contains("m_GameObject: {fileID: " + TapRootId + "}", audio);
            StringAssert.Contains("m_PlayOnAwake: 0", audio);
            StringAssert.Contains("m_Volume: 0.35", audio, "The local water level remains inspector-editable.");
            StringAssert.Contains("Loop: 1", audio);
            StringAssert.Contains("DopplerLevel: 0", audio);
            Assert.That(Regex.IsMatch(audio,
                @"(?s)panLevelCustomCurve:\s*serializedVersion: 2\s*m_Curve:\s*- serializedVersion: 3\s*time: 0\s*value: 1"),
                Is.True, "The Spatial Blend curve must be fully 3D at the source.");
            StringAssert.Contains("MinDistance: 0.5", audio);
            StringAssert.Contains("MaxDistance: 8", audio);

            AssertAddedToTapRoot(prefabInstance, FaucetId);
            AssertAddedToTapRoot(prefabInstance, AudioSourceId);

            StringAssert.Contains("--- !u!114 &" + FaucetId, scene);
            StringAssert.Contains("--- !u!82 &" + AudioSourceId, scene);
            StringAssert.Contains("--- !u!114 &1130038378", scene);
            StringAssert.Contains("--- !u!95 &652140272", scene);
            StringAssert.Contains("--- !u!198 &1586076941 stripped", scene);
        }

        [Test]
        public void TapCallbacks_ChangeWaterOnlyAfterTheTwoTouchLessonsComplete()
        {
            string scene = ReadScene();
            string turnOn = GetDocument(scene, "652237928");
            string turnOff = GetDocument(scene, "1654197283");
            StringAssert.Contains("_bindingId: washing-hand.turn-on-water.touch", turnOn);
            StringAssert.Contains("_bindingId: washing-hand.turn-off-water.touch", turnOff);

            AssertLegacyCallsDisabled(turnOn);
            AssertLegacyCallsDisabled(turnOff);
            AssertFaucetCompletionCall(turnOn, true);
            AssertFaucetCompletionCall(turnOff, false);
            Assert.That(Regex.IsMatch(GetSection(turnOn, "_onLessonActivated", "_onLessonCompleted"),
                @"m_Target: \{fileID: " + FaucetId + @"\}.*?m_CallState: 2", RegexOptions.Singleline), Is.False,
                "Activating the turn-on lesson must not open the tap.");
            Assert.That(Regex.IsMatch(GetSection(turnOff, "_onLessonActivated", "_onLessonCompleted"),
                @"m_Target: \{fileID: " + FaucetId + @"\}.*?m_CallState: 2", RegexOptions.Singleline), Is.False,
                "Activating the turn-off lesson must not change the tap.");

            string oldWaterTap = GetDocument(scene, WaterTapId);
            StringAssert.Contains("m_Script: {fileID: 11500000, guid: 2ec84b08549807641b8751a464a11ba1, type: 3}", oldWaterTap);
            StringAssert.Contains("m_Enabled: 0", oldWaterTap);
        }

        [Test]
        public void Scene_LocalObjectReferencesResolveIncludingStrippedPrefabHeaders()
        {
            string scene = ReadScene();
            MatchCollection headers = Regex.Matches(scene, @"(?m)^--- !u!\d+ &(?<id>-?\d+)(?: stripped)?[ \t]*$");
            var localIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match header in headers)
            {
                string id = header.Groups["id"].Value;
                Assert.That(localIds.Add(id), Is.True, "Duplicate scene document ID " + id);
            }

            MatchCollection pointers = Regex.Matches(scene, @"\{(?=[^{}]*\bfileID:\s*-?\d+)[^{}]*\}");
            foreach (Match pointer in pointers)
            {
                Match idMatch = Regex.Match(pointer.Value, @"\bfileID:\s*(?<id>-?\d+)");
                Assert.That(idMatch.Success, Is.True, "PPtr should include a fileID: " + pointer.Value);
                string id = idMatch.Groups["id"].Value;
                if (id == "0" || Regex.IsMatch(pointer.Value, @"\bguid:\s*"))
                    continue;

                Assert.That(localIds.Contains(id), Is.True,
                    "Local PPtr " + pointer.Value + " has no scene document header (stripped headers count too).");
            }
        }

        private static void AssertLegacyCallsDisabled(string sourceDocument)
        {
            string activation = GetSection(sourceDocument, "_onLessonActivated", "_onLessonCompleted");
            string completed = GetSection(sourceDocument, "_onLessonCompleted", "_interactorLayers");
            var legacyCalls = new List<string>();
            legacyCalls.AddRange(GetCallsTargeting(activation, WaterTapId));
            legacyCalls.AddRange(GetCallsTargeting(completed, WaterTapId));
            Assert.That(legacyCalls.Count, Is.EqualTo(3), "Each source retains its three legacy WaterTap callbacks.");
            foreach (string call in legacyCalls)
                StringAssert.Contains("m_CallState: 0", call);
        }

        private static void AssertFaucetCompletionCall(string sourceDocument, bool expectedOpen)
        {
            string completed = GetSection(sourceDocument, "_onLessonCompleted", "_interactorLayers");
            List<string> calls = GetCallsTargeting(completed, FaucetId);
            Assert.That(calls.Count, Is.EqualTo(1), "Each successful Touch completion has one faucet command.");
            string call = calls[0];
            StringAssert.Contains("m_MethodName: SetOpen", call);
            StringAssert.Contains("m_Mode: 6", call);
            StringAssert.Contains("m_BoolArgument: " + (expectedOpen ? "1" : "0"), call);
            StringAssert.Contains("m_CallState: 2", call);
        }

        private static List<string> GetCallsTargeting(string persistentCalls, string targetId)
        {
            var matching = new List<string>();
            MatchCollection calls = Regex.Matches(persistentCalls,
                @"(?ms)^\s*- m_Target:.*?(?=^\s*- m_Target:|\z)");
            foreach (Match call in calls)
            {
                if (Regex.IsMatch(call.Value, @"m_Target:\s*\{fileID:\s*" + targetId + @"\}"))
                    matching.Add(call.Value);
            }

            return matching;
        }

        private static string GetSection(string document, string startField, string endField)
        {
            Match match = Regex.Match(document,
                @"(?ms)^\s*" + Regex.Escape(startField) + @":\s*(?<section>.*?)(?=^\s*" + Regex.Escape(endField) + @":|\z)");
            Assert.That(match.Success, Is.True, "Missing serialized field " + startField);
            return match.Groups["section"].Value;
        }

        private static string GetDocument(string scene, string id)
        {
            Match match = Regex.Match(scene,
                @"(?ms)^--- !u!\d+ &" + Regex.Escape(id) + @"(?: stripped)?[ \t]*\r?\n.*?(?=^--- !u!|\z)");
            Assert.That(match.Success, Is.True, "Missing scene document " + id);
            return match.Value;
        }

        private static void AssertAddedToTapRoot(string prefabInstance, string addedObjectId)
        {
            string rootObject = @"- targetCorrespondingSourceObject: \{fileID: 919132149155446097, guid: a4fe84c3dfbd6484d8b4ee48832aa629,\s*type: 3\}\s*insertIndex: -1\s*addedObject: \{fileID: " + addedObjectId + @"\}";
            Assert.That(Regex.Matches(prefabInstance, rootObject, RegexOptions.Singleline).Count, Is.EqualTo(1),
                "Component " + addedObjectId + " must be added to the WaterTap prefab root.");
        }
    }
}
