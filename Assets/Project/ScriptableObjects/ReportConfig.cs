using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "New Report Config", menuName = "Report Config")]
public class ReportConfig : ScriptableObject
{
    public List<Topic> topics;
}

