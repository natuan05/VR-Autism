using System;
namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    public static class TtsCacheKeyV1
    {
        public static string Compute(string finalText, TtsProfileV1 profile)
        {
            return TtsCacheContractsV1.ComputeTtsCacheKey(finalText, profile);
        }
    }
}
