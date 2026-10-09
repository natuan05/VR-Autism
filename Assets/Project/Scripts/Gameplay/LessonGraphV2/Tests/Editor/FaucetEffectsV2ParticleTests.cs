using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Integration;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class FaucetEffectsV2ParticleTests
    {
        [Test]
        public void ApplyRunningWater_ActivatesInitiallyInactiveParticleObject()
        {
            var waterObject = new GameObject("inactive-water", typeof(ParticleSystem));
            var effectsObject = new GameObject("faucet-effects-output-test");
            waterObject.SetActive(false);
            effectsObject.SetActive(false);
            var particles = waterObject.GetComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            var effects = effectsObject.AddComponent<FaucetEffectsV2>();

            try
            {
                var waterField = typeof(FaucetEffectsV2).GetField("_runningWater",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var applyWater = typeof(FaucetEffectsV2).GetMethod("ApplyRunningWater",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(waterField, Is.Not.Null);
                Assert.That(applyWater, Is.Not.Null);
                waterField.SetValue(effects, particles);

                applyWater.Invoke(effects, new object[] { true });

                Assert.That(waterObject.activeInHierarchy, Is.True,
                    "The scene-authored inactive water object must be enabled before playback.");
                Assert.That(particles.isPlaying, Is.True, "Opening starts the real particle system.");
                Assert.That(effectsObject.activeSelf, Is.False,
                    "Only the assigned water visual is activated, not the owner component's object.");

                applyWater.Invoke(effects, new object[] { false });
                Assert.That(particles.isPlaying, Is.False);
                Assert.That(particles.particleCount, Is.EqualTo(0));

                applyWater.Invoke(effects, new object[] { true });
                Assert.That(waterObject.activeInHierarchy, Is.True);
                Assert.That(particles.isPlaying, Is.True, "Reopening restarts particle playback after stopping.");
            }
            finally
            {
                Object.DestroyImmediate(effectsObject);
                Object.DestroyImmediate(waterObject);
            }
        }
    }
}
