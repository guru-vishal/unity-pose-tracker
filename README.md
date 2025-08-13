# Unity Pose Tracker

A real-time avatar pose tracking system integrating **MediaPipe** for human pose estimation with a **Unity** client for 3D avatar animation.  
The system streams pose data from a Python server to Unity via WebSockets, mapping it to a Mixamo-rigged character in real time.

---

## 📦 Dependencies

### Python (Server)
- Python 3.9 - 3.11
- MediaPipe – Pose detection  
- OpenCV – Video capture and processing  
- `websockets` – Real-time data streaming to Unity  

### Unity (Client)
- Unity 2021.3 LTS (recommended)  
- NativeWebSocket – WebSocket client for Unity  
- Mixamo character model (FBX) with humanoid rig  
- Custom C# scripts for bone mapping and animation

---

## ⚙️ Setup Instructions

### 1. Clone the Repository or Download the zip file

    git clone https://github.com/guru-vishal/unity-pose-tracker.git

  • Or download the zip file and extract it

### 2. Python Server Setup

    cd "Python (Server)"
    pip install -r requirements.txt
    python pose_server.py

  • 	Ensure your webcam is connected (or update the script for video file input).
  • 	The server will start sending pose data over WebSocket.

### 3.  Unity Client Setup

  - Open the Unity (Client) folder in Unity Hub.
  - Import the NativeWebSocket package.
  - Assign your Mixamo rigged avatar in the scene.
  - Link the bone mapping script to your avatar’s Animator.
  - Update the WebSocket URL in the script to match the Python server’s address.
  - Press Play to see the avatar mimic your movements in real time.

### 4. Challenges Faced

  • 	Coordinate Mapping – Converting MediaPipe’s camera-space coordinates to Unity’s world-space.
  • 	Bone Orientation & Rotation Calibration – Aligning MediaPipe joint rotations with Mixamo’s humanoid rig.
  • 	Mirroring & Root Rotation – Correcting flipped movements and stabilizing the avatar’s facing direction.
  • 	Jitter Reduction – Implementing smoothing filters to reduce noisy pose data.

### 5. Time Taken

  Time taken
    - Setup complete (Unity + Python env ready): ~ 1 hour
    - Python pose server (MediaPipe + OpenCV + WebSockets): ~ 1 hour
    - Unity client receives pose data over WebSocket: ~ 45 minutes
    - Avatar retargeting: bone mapping, smoothing, root follow: ~ 7 hours
    - Fixes: mirroring, root rotation, head tilt, scale alignment: ~ 1 hour

