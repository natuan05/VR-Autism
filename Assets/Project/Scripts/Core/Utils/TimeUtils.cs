using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRAutism.Core
{
    public static class TimeUtils
    {
        public static double CurrentSecond => (DateTime.Now - DateTime.UnixEpoch).TotalSeconds;
        public static long CurrentDay => (long) CurrentSecond / 86400;
    }

}
