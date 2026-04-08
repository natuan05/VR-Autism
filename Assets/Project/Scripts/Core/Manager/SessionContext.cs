using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;



namespace VRAutism.Core
{
    public class SessionContext : MonoBehaviour
    {
        public static SessionContext Instance;
        
        public string SessionId { get; set; } = "";
        public string ChildId { get; set; } = "";
        
        private void Awake()
        {
            // Singleton Guard: Tránh việc 2 SessionContext cùng tồn tại
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject); 
                return; 
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
        }
    }
}
