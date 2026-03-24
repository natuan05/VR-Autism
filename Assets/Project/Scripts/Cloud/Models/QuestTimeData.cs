using System;

namespace VRAutism.Cloud.Models
{
    [Serializable]
    public class QuestTimeData
    {
        public int index;
        public string quest_name;
        public double response_time;
        public int hint_count;
    }
}
