using System;
using System.Collections.Generic;

namespace VRAutism.Cloud.Models
{
    [Serializable]
    public class LessonTimeData
    {
        public String lesson_name;
        public String level_name;
        public int lesson_index;
        public String lesson_id;
        public int level_index;
        public double duration;
        public bool hasQuest;
        public String start_time;
        public String finish_time;
        public String video_url;
        public List<QuestTimeData> quest_list;
        public List<SkillsData> skills = new List<SkillsData>
        {
            new SkillsData
            {
                initiation = 0,
                negotiation = 0,
                self_identity = 0,
                cognitive_flexibility = 0
            }
        };

        public String device_id;
        public String type;
        public int score;
    }
}
