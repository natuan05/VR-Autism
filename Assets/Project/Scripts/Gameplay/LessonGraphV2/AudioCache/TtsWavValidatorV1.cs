using System;
using VRAutism.Gameplay.LessonGraphV2.AudioCache;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    /// <summary>Strict adapter over the shared PCM16/24 kHz WAV contract.</summary>
    public static class TtsWavValidatorV1
    {
        public const int MaxWavBytes = TtsCacheContractsV1.MaxWavBytes;

        public static bool TryValidate(byte[] bytes, out WavMetadataV1 metadata, out string reason)
        {
            metadata = default(WavMetadataV1);
            reason = string.Empty;
            try
            {
                metadata = TtsCacheContractsV1.ValidateWav(bytes);
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        public static WavMetadataV1 Validate(byte[] bytes) => TtsCacheContractsV1.ValidateWav(bytes);
    }
}
