using UnityEngine;
using System;
using System.Collections.Generic;

namespace VRAutism.Core
{
    public class EventChannel : MonoBehaviour 
    {
        #region Singleton
        private static EventChannel _instance;
        private static bool _isQuitting = false;

        public static EventChannel Instance 
        {
            get
            {
                if (_instance == null && !_isQuitting) 
                {
                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<EventChannel>();
                    singletonObject.name = "Singleton - EventChannel";
                    Dz.Log("Create singleton : {0}", singletonObject.name);

                    DontDestroyOnLoad(singletonObject);
                }
                return _instance;
            }
        }

        public static bool IsInstance() 
        {
            return _instance != null;
        }

        private void Awake() 
        {
            if (_instance != null && _instance.GetInstanceID() != this.GetInstanceID()) 
            {
                Dz.Log($"An instance of EventChannel already exist: <{_instance.name}>, So destroy this instance : <{name}>");
                Destroy(gameObject);
            } 
            else 
            {
                _instance = this as EventChannel;
            }
        }

        private void OnDestroy() {
            if (_instance == this) 
            {
                ClearAllListener();
                _instance = null;
            }
        }

        private void OnApplicationQuit() 
        {
            _isQuitting = true;
        }

        #endregion

        #region Field
        Dictionary<EventID, Action<object>> _listeners = new Dictionary<EventID, Action<object>>();
        #endregion

        #region Subscribe Listeners, Send Event, Unsubscribe Listen
        /// <summary> 
        /// Subscribe Listeners for EventID
        /// </summary>
        /// <param name="eventID">EventID that object want to listen</param>
        /// <param name="callback">Callback will be invoked when this eventID be raised</param>
        public void SubscribeListener(EventID eventID, Action<object> callback)
        {
            // checking params
            Dz.Assert(callback != null, $"AddListener, event {eventID.ToString()}, callback = null !!");
            Dz.Assert(eventID != EventID.None, "SubcribeListener, event = None !!");

            // check if listener exist in distionary
            if (_listeners.ContainsKey(eventID))
            {
                // add callback to our collection
                _listeners[eventID] += callback;
            }
            else
            {
                // add new key-value pair
                _listeners.Add(eventID, null);
                _listeners[eventID] += callback;
            }
        }

        /// <summary>
        /// Posts the event. This will notify all listener that register for this event
        /// </summary>
        /// <param name="eventID">EventID.</param>
        /// <param name="sender">Sender, in some case, the Listener will need to know who send this message.</param>
        /// <param name="param">Parameter. Can be anything (struct, class ...), Listener will make a cast to get the data</param>
        public void SendEvent(EventID eventID, object param = null)
        {
            if (!_listeners.ContainsKey(eventID))
            {
                Dz.Log("No listeners for this event : {0}", eventID);
                return;
            }

            // posting event
            var callbacks = _listeners[eventID];
            // if there's no listener remain, then do nothing
            if (callbacks != null)
            {
                callbacks(param);
            }
            else
            {
                Dz.Log("SendEvent {0}, but no listener remain, Remove this key", eventID);
                _listeners.Remove(eventID);
            }
        }

        /// <summary>
        /// Unsubscribe the listener.
        /// </summary>
        /// <param name="eventID">EventID.</param>
        /// <param name="callback">Callback.</param>
        public void UnsubscribeListener(EventID eventID, Action<object> callback)
        {
            // checking params
            Dz.Assert(callback != null, $"UnsubscribeListener, event {eventID.ToString()}, callback = null !!");
            Dz.Assert(eventID != EventID.None, "UnsubcribeListener, event = None !!");

            if (_listeners.ContainsKey(eventID))
            {
                _listeners[eventID] -= callback;
                //_listeners.Remove(eventID);
            }
            else
            {
                Dz.Warning(false, "UnsubscribeListener, not found key : " + eventID);
            }
        }

        /// <summary>
        /// Clears all the listener.
        /// </summary>
        public void ClearAllListener()
        {
            _listeners.Clear();
        }
        #endregion
    }

    #region Extension class
    /// <summary>
    /// Delare some "shortcut" for using EventChannel easier
    /// </summary>
    public static class EventChannelExtension
    {
        /// Use for registering with EventsManager
        public static void SubscribeListener(this MonoBehaviour listener, EventID eventID, Action<object> callback)
        {
            if (EventChannel.Instance != null)
                EventChannel.Instance.SubscribeListener(eventID, callback);
        }

        /// Post event with param
        public static void SendEvent(this MonoBehaviour listener, EventID eventID, object param)
        {
            if (EventChannel.Instance != null)
                EventChannel.Instance.SendEvent(eventID, param);
        }

        /// Post event with no param (param = null)
        public static void SendEvent(this MonoBehaviour sender, EventID eventID)
        {
            if (EventChannel.Instance != null)
                EventChannel.Instance.SendEvent(eventID, null);
        }
        
        /// Remove event when destroy game object
        public static void UnsubscribeListener(this MonoBehaviour listener, EventID eventID, Action<object> callback)
        {
            if (EventChannel.Instance != null)
                EventChannel.Instance.UnsubscribeListener(eventID, callback);
        }
    }

    #endregion

    public enum EventID 
    {
        None,
        DialogueEnding,
        ToggleTheDoor,
        // Menu Scene
        OnTriggerProcessEnter,
        OnTriggerProcessExit,
        PlaySound,
        PauseSound,
        PlaySoundLoop,
        // Washing Hand Scene
        ToggleFaucet
    }
}