using System;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;

[Serializable]
public class Landmark
{
    public int id;
    public float x;
    public float y;
    public float z;
    public float visibility;
}

[Serializable]
public class PosePayload
{
    public double timestamp;
    public int image_w;
    public int image_h;
    public float mid_hip_z;
    public float fps;
    public List<Landmark> landmarks;
}

public class PoseReceiver : MonoBehaviour
{
    private WebSocket websocket;
    public PosePayload latestPose;

    // Simple smoothing params
    private PosePayload smoothedPose;
    private float smoothingFactor = 0.8f;

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8765");

        websocket.OnOpen += () =>
        {
            Debug.Log("[WS] Connected to Python server");
        };

        websocket.OnMessage += (bytes) =>
        {
            try
            {
                string message = System.Text.Encoding.UTF8.GetString(bytes);
                PosePayload newPose = JsonUtility.FromJson<PosePayload>(message);
                if (newPose?.landmarks != null && newPose.landmarks.Count > 0)
                {
                    // Smooth landmarks between frames
                    if (smoothedPose == null)
                        smoothedPose = newPose;
                    else
                        SmoothPose(smoothedPose, newPose);

                    latestPose = smoothedPose;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WS] JSON parse error: " + ex.Message);
            }
        };

        websocket.OnError += (err) =>
        {
            Debug.LogError("[WS] Error: " + err);
        };

        websocket.OnClose += (code) =>
        {
            Debug.Log("[WS] Closed with code: " + code);
        };

        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
    }

    async void OnApplicationQuit()
    {
        await websocket.Close();
    }

    // Smooth pose landmarks by lerping each float coordinate
    private void SmoothPose(PosePayload smooth, PosePayload fresh)
    {
        for (int i = 0; i < smooth.landmarks.Count; i++)
        {
            Landmark s = smooth.landmarks[i];
            Landmark f = fresh.landmarks[i];
            s.x = Mathf.Lerp(s.x, f.x, 1 - smoothingFactor);
            s.y = Mathf.Lerp(s.y, f.y, 1 - smoothingFactor);
            s.z = Mathf.Lerp(s.z, f.z, 1 - smoothingFactor);
            s.visibility = Mathf.Lerp(s.visibility, f.visibility, 1 - smoothingFactor);
        }

        smooth.timestamp = fresh.timestamp;
        smooth.image_w = fresh.image_w;
        smooth.image_h = fresh.image_h;
        smooth.mid_hip_z = fresh.mid_hip_z;
        smooth.fps = fresh.fps;
    }

    // Convert normalized landmark coordinates to Unity world space.
    // Adjust Y and Z axes based on your avatar’s setup.
    public Vector3 GetWorldPosition(int landmarkId, float scale = 1.0f)
    {
        if (latestPose == null || latestPose.landmarks == null)
            return Vector3.zero;

        Landmark lm = latestPose.landmarks.Find(l => l.id == landmarkId);
        if (lm == null) return Vector3.zero;

        // Center at 0 and flip Y (because MediaPipe origin is top-left, Unity is bottom-left)
        float x = (lm.x - 0.5f) * scale;
        float y = (0.5f - lm.y) * scale;
        float z = -lm.z * scale; // Invert z to match forward in Unity

        return transform.TransformPoint(new Vector3(x, y, z));
    }
}
