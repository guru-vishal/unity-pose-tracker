# pose_server.py
"""
Real-time MediaPipe Pose -> WebSocket broadcaster
- Run inside your project's venv:
    pip install mediapipe opencv-python websockets
- Start:
    python pose_server.py
- Connect a client to: ws://localhost:8765

Controls:
- Press 'q' in the camera window to stop and quit.
"""

import threading
import time
import json
import asyncio
from typing import Optional

try:
    import cv2
    import mediapipe as mp
    import websockets
except Exception as e:
    print("Missing dependency or import error:", e)
    print("Install required packages inside your venv:\n  pip install mediapipe opencv-python websockets")
    raise

# WebSocket server settings
WS_HOST = "0.0.0.0"
WS_PORT = 8765

mp_pose = mp.solutions.pose
mp_drawing = mp.solutions.drawing_utils

TARGET_FPS = 30
FRAME_DELAY = 1.0 / TARGET_FPS

# Shared state
CONNECTED = set()  # set of websocket connections


def landmarks_to_normalized_list(landmarks):
    out = []
    for idx, lm in enumerate(landmarks):
        out.append({
            "id": idx,
            "x": lm.x,  # normalized [0..1]
            "y": lm.y,  # normalized [0..1]
            "z": lm.z,  # relative depth in MP normalized coords
            "visibility": getattr(lm, "visibility", None)
        })
    return out


def compute_mid_hip_z(landmarks):
    try:
        return (landmarks[23].z + landmarks[24].z) / 2.0
    except Exception:
        return 0.0


def camera_worker(last_payload: dict, stop_event: threading.Event, camera_index: int = 0):
    """
    Runs in a background thread, updates last_payload in-place.
    Shows a debug OpenCV window and sets stop_event when 'q' pressed.
    """
    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        print(f"[CAM] ERROR: Could not open webcam (index {camera_index}).")
        stop_event.set()
        return

    with mp_pose.Pose(
        static_image_mode=False,
        model_complexity=1,
        enable_segmentation=False,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5
    ) as pose:
        print("[CAM] Camera worker started. Press 'q' in window to quit.")
        prev = time.time()
        try:
            while not stop_event.is_set():
                t0 = time.time()
                ret, frame = cap.read()
                if not ret:
                    print("[CAM] Failed reading frame from webcam.")
                    break

                # Mirror for natural interaction
                frame_rgb = cv2.cvtColor(cv2.flip(frame, 1), cv2.COLOR_BGR2RGB)
                h, w, _ = frame_rgb.shape

                results = pose.process(frame_rgb)

                if results.pose_landmarks:
                    lm_list = results.pose_landmarks.landmark
                    mid_hip_z = compute_mid_hip_z(lm_list)
                    normalized_landmarks = landmarks_to_normalized_list(lm_list)

                    # update shared payload (simple atomic replace since same thread writes)
                    last_payload["timestamp"] = time.time()
                    last_payload["landmarks"] = normalized_landmarks
                    last_payload["image_w"] = w
                    last_payload["image_h"] = h
                    last_payload["mid_hip_z"] = mid_hip_z
                    last_payload["fps"] = 1.0 / max(1e-4, time.time() - prev)
                else:
                    last_payload["landmarks"] = None

                # debug drawing & display (flip back)
                debug_frame = cv2.flip(frame, 1)
                if results.pose_landmarks:
                    mp_drawing.draw_landmarks(
                        debug_frame,
                        results.pose_landmarks,
                        mp_pose.POSE_CONNECTIONS,
                        mp_drawing.DrawingSpec(thickness=2, circle_radius=2),
                        mp_drawing.DrawingSpec(thickness=2, circle_radius=2),
                    )

                cv2.putText(debug_frame, "Press 'q' to quit", (10, 30),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
                cv2.imshow("MediaPipe Pose (websocket server)", debug_frame)

                prev = time.time()

                # small sleep to control loop roughly
                elapsed = time.time() - t0
                to_sleep = FRAME_DELAY - elapsed
                if to_sleep > 0:
                    time.sleep(to_sleep)

                # Check key in the same thread (required for imshow to be responsive)
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    stop_event.set()
                    break
        finally:
            cap.release()
            cv2.destroyAllWindows()
            stop_event.set()
            print("[CAM] Camera worker stopped.")


async def broadcast_loop(get_payload_fn, stop_event: threading.Event):
    """Collect payloads and broadcast to connected websockets at TARGET_FPS."""
    while not stop_event.is_set():
        start = time.time()
        payload = get_payload_fn()
        if payload is None or payload.get("landmarks") is None:
            # nothing to send currently
            await asyncio.sleep(0.001)
        else:
            message = json.dumps(payload)
            webs = list(CONNECTED)
            if webs:
                send_coros = []
                for ws in webs:
                    try:
                        send_coros.append(ws.send(message))
                    except Exception:
                        pass
                if send_coros:
                    await asyncio.gather(*send_coros, return_exceptions=True)

        elapsed = time.time() - start
        to_sleep = FRAME_DELAY - elapsed
        if to_sleep > 0:
            await asyncio.sleep(to_sleep)
        else:
            await asyncio.sleep(0.001)


async def ws_handler(ws):
    """Handle new websocket client connections."""
    addr = None
    try:
        peer = ws.remote_address
        addr = peer
        print(f"[WS] Client connected: {peer}")
    except Exception:
        print("[WS] Client connected (address unknown)")

    CONNECTED.add(ws)
    try:
        # keep the connection alive; if client sends messages we ignore them for now
        async for _ in ws:
            pass
    except websockets.exceptions.ConnectionClosedOK:
        pass
    except Exception as e:
        print("[WS] Connection error:", e)
    finally:
        CONNECTED.discard(ws)
        print(f"[WS] Client disconnected: {addr}")


async def start_websocket_server(get_payload_fn, stop_event: threading.Event):
    """Start websockets server and broadcasting task; wait until stop_event set."""
    server = await websockets.serve(ws_handler, WS_HOST, WS_PORT)
    print(f"[WS] Server listening on ws://{WS_HOST}:{WS_PORT}")

    broadcaster = asyncio.create_task(broadcast_loop(get_payload_fn, stop_event))

    # Wait until stop_event is set (poll)
    try:
        while not stop_event.is_set():
            await asyncio.sleep(0.1)
    finally:
        # shutdown: close websockets and wait
        print("[WS] Shutting down server, closing clients...")
        # close connected clients
        for ws in list(CONNECTED):
            try:
                await ws.close()
            except Exception:
                pass
        server.close()
        await server.wait_closed()
        broadcaster.cancel()
        try:
            await broadcaster
        except asyncio.CancelledError:
            pass
        print("[WS] Server stopped.")


def main():
    last_payload = {"timestamp": None, "landmarks": None}
    stop_event = threading.Event()

    # start camera thread
    cam_thread = threading.Thread(target=camera_worker, args=(last_payload, stop_event, 0), daemon=True)
    cam_thread.start()

    # helper to get payload for broadcaster
    def get_payload():
        # return a shallow copy so async code doesn't mutate while sending
        if last_payload["landmarks"] is None:
            return None
        return dict(last_payload)

    # run websocket server in asyncio loop and wait for stop_event
    try:
        asyncio.run(start_websocket_server(get_payload, stop_event))
    except KeyboardInterrupt:
        stop_event.set()
    finally:
        stop_event.set()
        # ensure camera thread ends
        cam_thread.join(timeout=2)
        print("Exiting.")


if __name__ == "__main__":
    main()
