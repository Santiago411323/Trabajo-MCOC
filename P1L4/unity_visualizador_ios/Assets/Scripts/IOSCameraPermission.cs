using System.Runtime.InteropServices;

/// <summary>Read the actual AVFoundation authorization state without equating pending with denied.</summary>
public static class IOSCameraPermission
{
    public enum State { MissingUsageDescription = -1, NotDetermined = 0, Restricted = 1, Denied = 2, Authorized = 3 }
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int MCOC_CameraAuthorizationStatus();
    [DllImport("__Internal")] private static extern void MCOC_RequestCameraAuthorization();
    [DllImport("__Internal")] private static extern int MCOC_CameraAuthorizationPending();
#endif
    public static State Current
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return (State)MCOC_CameraAuthorizationStatus();
#else
            return State.Authorized; // XR Simulation never requests a physical camera.
#endif
        }
    }
    public static bool Pending
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return MCOC_CameraAuthorizationPending() != 0;
#else
            return false;
#endif
        }
    }
    public static void Request()
    {
#if UNITY_IOS && !UNITY_EDITOR
        MCOC_RequestCameraAuthorization();
#endif
    }
}
